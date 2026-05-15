# Research: Chat Service (RAG-based AI Q&A)

## Overview

The chat service is a Retrieval-Augmented Generation (RAG) system that allows users to ask natural-language questions about ETFs and equities. It retrieves semantically relevant documents stored in PostgreSQL (via the `pgvector` extension), builds an augmented prompt, and submits it to a locally-hosted Ollama LLM to generate a grounded answer.

The system is intentionally narrow in scope: it answers questions using only pre-seeded document embeddings — it does not access live market data, portfolio data, or external APIs at query time.

---

## Entry Points

### `POST /api/chat`
Defined in `ChatController` (`src/EtfInsight.Api/Controllers/ChatController.cs`).

Accepts a JSON body `{ "question": "..." }`. Validates the question is non-empty, then:
1. Delegates to `IChatService.AskAiAsync` (which internally runs the full RAG pipeline).
2. **Independently** re-generates the question embedding and re-runs the semantic search to attach `sources` to the response.

Returns: `{ question, answer, sources: [{ ticker, similarity, excerpt }], timestamp }`.

### `GET /api/chat/suggestions`
Also in `ChatController`. Returns a hardcoded list of example questions (in Italian). No service or DB involvement.

### `POST /api/search/seed`
Defined in `SemanticSearchController` (`src/EtfInsight.Api/Controllers/SemanticSearchController.cs`).

Developer-facing endpoint. Contains a hardcoded dictionary of 13 ticker → Italian description pairs (ETFs and equities). Generates embeddings for each and upserts them into `etf_documents`. No authentication guard.

### `POST /api/search/query`
Also in `SemanticSearchController`. Accepts `{ "query": "...", "limit": 5 }`. Generates an embedding for the query and returns raw similarity-ranked results from `etf_documents`. Bypasses the LLM entirely — this is a direct semantic search endpoint.

---

## Core Data Flow

### Ask (`POST /api/chat`)

```
ChatController.Ask
  └─ IChatService.AskAiAsync(question)                  [OllamaChatService]
       ├─ IEmbeddingGenerator.GenerateEmbeddingAsync     [OllamaEmbeddingService]
       │    └─ POST /api/embeddings → Ollama (nomic-embed-text)
       │         returns float[]
       ├─ ISemanticSearchRepository.SearchAsync(embedding, limit=5)  [DapperSemanticSearchRepository]
       │    └─ pgvector cosine distance query on etf_documents
       │         returns IEnumerable<SearchResult>
       ├─ BuildAugmentedPrompt(question, docs)
       │    └─ Constructs prompt string with AVAILABLE CONTEXT + INSTRUCTIONS + USER QUESTION
       └─ GenerateResponseAsync(prompt)
            └─ POST /api/generate → Ollama (llama3.2)
                 Stream=false, Temperature=0.1
                 returns string answer

  [Back in ChatController]
  └─ IEmbeddingGenerator.GenerateEmbeddingAsync(question)   ← DUPLICATE call
  └─ ISemanticSearchRepository.SearchAsync(embedding, limit=5)  ← DUPLICATE call
       → used only to populate `sources` field in the HTTP response
```

### Seed (`POST /api/search/seed`)

```
SemanticSearchController.SeedData
  └─ for each (ticker, description) in hardcoded dict:
       ├─ IEmbeddingGenerator.GenerateEmbeddingAsync(description)
       └─ ISemanticSearchRepository.SaveEmbeddingAsync(ticker, description, embedding)
            └─ INSERT ... ON CONFLICT (ticker) DO UPDATE on etf_documents
```

---

## Key Components

### `OllamaChatService` (`src/EtfInsight.Infrastructure/Services/OllamaChatService.cs`)
- Implements `IChatService`.
- Registered as `Scoped`.
- Owns the full RAG pipeline: embed → retrieve → augment → generate.
- Uses `IHttpClientFactory` with the named client `"Ollama"`. Sets `BaseAddress` from `AISettings.OllamaUrl` and `Timeout = 60s`.
- Prompt is assembled in `BuildAugmentedPrompt`. Language is adaptive ("answer in the same language as the USER QUESTION").
- `GenerateResponseAsync` POSTs to `/api/generate` with `Stream=false` and `Temperature=0.1`. Throws `InvalidOperationException` on empty response.
- Error handling: wraps all exceptions in a new `InvalidOperationException` with a user-readable message. The original exception is preserved as inner exception.

### `OllamaEmbeddingService` (`src/EtfInsight.Infrastructure/Services/OllamaEmbeddingService.cs`)
- Implements `IEmbeddingGenerator`.
- Registered as `Scoped`.
- POSTs to `/api/embeddings` with `{ model, prompt }`. Returns `float[]`.
- Timeout: 30s (shorter than the chat timeout).
- Catches `HttpRequestException` separately for a clearer "Is Ollama running?" message.
- `OllamaEmbeddingResponse` is a `public` nested class (inconsistent with `OllamaEmbeddingRequest` which is `internal`).

### `DapperSemanticSearchRepository` (`src/EtfInsight.Infrastructure/Repositories/DapperSemanticSearchRepository.cs`)
- Implements `ISemanticSearchRepository`.
- Registered as `Scoped`. Takes `IDbConnection` (Npgsql, also scoped).
- `SearchAsync`: uses cosine distance operator `<=>` on a `vector` column. Similarity is computed as `1 - distance`. Results are **not filtered** by a minimum similarity threshold — all rows are returned up to `limit`, regardless of relevance quality.
- `SaveEmbeddingAsync`: upserts on `ticker`. Serialises the float array as a string literal `[f1,f2,...]` cast to `::vector`. Hardcodes `metadata` to `{"source": "manual_seed", "version": "1.0"}` and `is_mandatory = false` for all embeddings.
- No `CancellationToken` support on either method (interface does not define it).

### `ChatController` (`src/EtfInsight.Api/Controllers/ChatController.cs`)
- No authentication or authorisation attributes. All endpoints are public.
- Injects both `IChatService` and the lower-level `IEmbeddingGenerator`/`ISemanticSearchRepository` directly — this is unusual and creates a dual responsibility: the controller both delegates to the service and re-runs part of the service's work to populate `sources`.
- `ChatRequest` is defined as a non-record `class` at the bottom of the file, with no validation attributes beyond the manual `IsNullOrWhiteSpace` check.

### `SemanticSearchController` (`src/EtfInsight.Api/Controllers/SemanticSearchController.cs`)
- No authentication. `/api/search/seed` is an unauthenticated write endpoint that modifies the vector store. Intended as a developer/admin tool but unguarded in production.
- `SearchRequest.Limit` is `int?`; no upper-bound validation — a caller could request an arbitrarily large number of results.

### `AISettings` (`src/EtfInsight.Core/Configuration/AISettings.cs`)
- Bound from config section `"AI"` in `Program.cs`.
- Defaults: `OllamaUrl = "http://localhost:11434"`, `EmbeddingModel = "nomic-embed-text"`, `ChatModel = "llama3.2"`, `EmbeddingDimensions = 768`.
- `EmbeddingDimensions` is defined but **never consumed** anywhere in the codebase.

### `IChatService` (`src/EtfInsight.Core/Services/IChatService.cs`)
- Single method: `Task<string> AskAiAsync(string question)`. No `CancellationToken` parameter.

### `ISemanticSearchRepository` (`src/EtfInsight.Core/Interfaces/ISemanticSearchRepository.cs`)
- Two methods: `SaveEmbeddingAsync` and `SearchAsync`. Neither accepts a `CancellationToken`.

---

## External Dependencies

### Ollama (HTTP)
- Named `HttpClient` `"Ollama"` registered in `Program.cs` (no base address set at registration; set per-instance inside each service constructor).
- Two endpoints used:
  - `POST /api/embeddings` — embedding generation (model: `nomic-embed-text`, 768 dimensions)
  - `POST /api/generate` — text generation (model: `llama3.2`, stream=false)
- Assumed to be running locally or at the configured `OllamaUrl`. No health check, circuit breaker, or retry policy.

### PostgreSQL + pgvector
- `etf_documents` table with columns: `ticker` (unique), `content`, `embedding` (vector type), `metadata` (jsonb), `is_mandatory`, `created_at`.
- Vector similarity search uses the `<=>` (cosine distance) operator provided by `pgvector`.
- Dapper is used directly with raw SQL. No EF Core involvement for this feature.
- `IDbConnection` is scoped (one `NpgsqlConnection` per HTTP request).

---

## Existing Patterns & Conventions

- All AI-facing services live in `EtfInsight.Infrastructure.Services` and implement interfaces defined in `EtfInsight.Core`.
- DI registration is manual in `Program.cs`; all AI services are `Scoped`.
- Named `HttpClient` pattern is used consistently (`"Ollama"`, `"Airflow"`, `"OpenFigi"`).
- Repository layer uses Dapper with raw SQL (no EF Core for read-heavy paths).
- `AISettings` is the single configuration object for all AI-related settings, bound via `IOptions<AISettings>`.

---

## Potential Issues

1. **Duplicate embedding + search in `ChatController.Ask`** (`ChatController.cs`, lines 55–56`): The controller independently regenerates the embedding and re-runs `SearchAsync` solely to populate the `sources` field. This doubles the cost of every chat request (two embedding calls to Ollama, two DB queries). The service already has the results internally but discards them.

2. **No `CancellationToken` propagation**: `IChatService.AskAiAsync`, `IEmbeddingGenerator.GenerateEmbeddingAsync`, and `ISemanticSearchRepository` methods all omit `CancellationToken`. Long-running Ollama calls cannot be cancelled when the HTTP client disconnects.

3. **No minimum similarity threshold in `SearchAsync`**: All documents up to `limit` are returned regardless of relevance. Very low-relevance documents will be injected into the LLM prompt, potentially degrading answer quality or causing hallucination.

4. **Unauthenticated `/api/search/seed`**: This endpoint modifies the vector store. It is unprotected and callable by anyone in the current configuration.

5. **`EmbeddingDimensions` setting is unused**: `AISettings.EmbeddingDimensions = 768` is never read. The actual dimension is determined by whatever the Ollama model returns. If the model changes, the schema may break silently.

6. **`OllamaEmbeddingResponse` is `public`** (`OllamaEmbeddingService.cs`, line 84): A response DTO for an internal HTTP call is unnecessarily surfaced as a public type on the class.

7. **No upper-bound validation on `SearchRequest.Limit`** (`SemanticSearchController.cs`, line 147): An unbounded value flows directly into the SQL `LIMIT` clause.

8. **`HttpClient` base address set in constructor, not at registration**: Both `OllamaChatService` and `OllamaEmbeddingService` call `_httpClient.BaseAddress = new Uri(...)` after obtaining the client from the factory. Mutating a shared `HttpClient`'s `BaseAddress` post-construction is not safe if the named client is reused across scopes (though it works because new instances are created per named client).

9. **System prompt is hardcoded as an inline string** (`OllamaChatService.cs`, lines 101–109): Prompt engineering is embedded in the service. Iterating on the prompt requires recompilation and redeployment.

10. **`SearchResult` is a mutable class, not a record** (`SearchResult.cs`): Inconsistent with the project's preference for records for DTOs.

---

## Open Questions

1. **Is multi-user / multi-tenant context intended for the chat feature?** Currently there is no portfolio or user context injected into the prompt — all users see the same document corpus.

2. **What is the long-term document corpus strategy?** The only way to add documents today is via the `/api/search/seed` developer endpoint with a hardcoded dictionary. Is there a planned ingestion pipeline (e.g., from ETF metadata, fund factsheets)?

3. **Should the chat feature support conversational history (multi-turn)?** The current implementation is fully stateless — each call is independent with no message history.

4. **Is there a plan to replace Ollama with a hosted LLM API** (e.g., OpenAI, Azure OpenAI)? The current abstraction (`IChatService`, `IEmbeddingGenerator`) is thin enough to support a swap, but the prompt format and model-specific assumptions are baked in.

5. **What is the intended embedding dimension?** `AISettings.EmbeddingDimensions = 768` matches `nomic-embed-text`, but this is never enforced at schema creation or at runtime. If a different model is configured, silent failures or type mismatches could occur in the vector column.

