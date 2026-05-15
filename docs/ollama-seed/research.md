# Research: Ollama Seed (Semantic Search & RAG)

## Overview

The "ollama-seed" system is the local AI layer of the platform. It provides two capabilities:

1. **Embedding generation & seeding** — converts ETF/equity descriptions into 768-dimensional vectors stored in PostgreSQL (pgvector), enabling semantic similarity search.
2. **RAG-based chat** — answers user questions by embedding the query, retrieving the most relevant documents via cosine similarity, and feeding them as context to a local LLM (llama3.2) via Ollama.

All AI operations run entirely on localhost through Ollama. No external API calls.

## Entry Points

| Route | Method | Controller | Purpose |
|---|---|---|---|
| `POST /api/search/seed` | POST | `SemanticSearchController` | Seeds the `etf_documents` table with hardcoded ETF/equity descriptions and their embeddings |
| `POST /api/search/query` | POST | `SemanticSearchController` | Performs semantic similarity search against stored embeddings |
| `POST /api/chat` | POST | `ChatController` | RAG pipeline: embed question → retrieve docs → augmented prompt → LLM answer |
| `GET /api/chat/suggestions` | GET | `ChatController` | Returns a static list of suggested questions (Italian) |

## Core Data Flow

### Seed flow (`POST /api/search/seed`)

1. Controller holds a hardcoded `Dictionary<string, string>` of 13 ticker→description pairs (8 ETFs + 5 equities). Descriptions are in Italian.
2. For each entry, calls `IEmbeddingGenerator.GenerateEmbeddingAsync(description)`.
3. `OllamaEmbeddingService` POSTs to `http://localhost:11434/api/embeddings` with model `nomic-embed-text` and the description as prompt.
4. Ollama returns a `float[]` embedding (768 dimensions).
5. `DapperSemanticSearchRepository.SaveEmbeddingAsync` serialises the float array to a string `[0.1,0.2,...]` and executes an `INSERT ... ON CONFLICT (ticker) DO UPDATE` into `etf_documents`.
6. Metadata is hardcoded: `{"source": "manual_seed", "version": "1.0"}`, `is_mandatory = false`.

### Query flow (`POST /api/search/query`)

1. Validates `request.Query` is non-empty.
2. Embeds the query text via `OllamaEmbeddingService`.
3. `DapperSemanticSearchRepository.SearchAsync` runs a pgvector cosine distance query: `1 - (embedding <=> @QueryEmbedding::vector)` ordered ascending by distance, limited to `request.Limit ?? 5`.
4. Returns ticker, content, and similarity score.

### Chat flow (`POST /api/chat`)

1. Validates `request.Question`.
2. Delegates to `OllamaChatService.AskAiAsync(question)`:
   - Embeds the question.
   - Retrieves top-5 documents via semantic search.
   - Builds an augmented prompt with context and instructions (system prompt is inline, not configurable).
   - POSTs to `http://localhost:11434/api/generate` with model `llama3.2`, `stream: false`, `temperature: 0.1`.
3. **Back in the controller**, generates the embedding a second time and searches again to return sources alongside the answer. This duplicates the work already done inside `OllamaChatService`.

## Key Components

### `AISettings` — `EtfInsight.Core/Configuration/AISettings.cs`
- POCO bound from `appsettings.json` section `AI`.
- Fields: `OllamaUrl`, `EmbeddingModel` (`nomic-embed-text`), `ChatModel` (`llama3.2`), `EmbeddingDimensions` (768).
- `EmbeddingDimensions` is never used at runtime — it exists as documentation only.

### `IEmbeddingGenerator` / `OllamaEmbeddingService` — `Infrastructure/Services/OllamaEmbeddingService.cs`
- Single method: `GenerateEmbeddingAsync(string input) → float[]`.
- Creates a new `HttpClient` from `IHttpClientFactory` in the constructor, sets `BaseAddress` and `Timeout` (30s) on every instantiation.
- Uses Ollama's legacy `/api/embeddings` endpoint (singular prompt field).
- No `CancellationToken` support.
- Response DTO `OllamaEmbeddingResponse` is a nested public class; request DTO `OllamaEmbeddingRequest` is internal at namespace level — inconsistent.

### `ISemanticSearchRepository` / `DapperSemanticSearchRepository` — `Infrastructure/Repositories/DapperSemanticSearchRepository.cs`
- `SaveEmbeddingAsync(ticker, content, embedding)` — upserts by ticker. Embedding serialised manually with `InvariantCulture`.
- `SearchAsync(queryEmbedding, limit)` — cosine distance search. No filtering, no pagination, no `CancellationToken`.
- Uses raw `IDbConnection` (Dapper), not EF Core.

### `IChatService` / `OllamaChatService` — `Infrastructure/Services/OllamaChatService.cs`
- `AskAiAsync(string question) → string`.
- Performs the full RAG pipeline internally: embed → search → augment → generate.
- Builds prompt inline with string concatenation (no newlines between sections — `INSTRUCTIONS:` runs into the context block).
- `temperature: 0.1` hardcoded.
- No `CancellationToken` support.
- `OllamaGenerateRequest` and `OllamaChatResponse` are internal classes at namespace level.

### `SemanticSearchController` — `Api/Controllers/SemanticSearchController.cs`
- `SeedData()` — no auth, no idempotency key, no input parameters. Hardcoded seed data lives in the controller.
- `Query()` — accepts `SearchRequest` (nested public class) with `Query` and optional `Limit`.
- No `CancellationToken` in any action method.

### `ChatController` — `Api/Controllers/ChatController.cs`
- `Ask()` — injects `IEmbeddingGenerator` and `ISemanticSearchRepository` directly despite also injecting `IChatService` which already uses them internally. This causes the embedding to be generated twice per request.
- `GetSuggestions()` — returns hardcoded Italian question strings.
- No `CancellationToken` in any action method.

### Schema — `src/db/04_etf_documents_schema.sql`
- Table `etf_documents`: UUID PK, `ticker VARCHAR(20) NOT NULL UNIQUE`, `content TEXT`, `metadata JSONB`, `embedding vector(768)`, `created_at`, `is_mandatory`.
- FK to `etf_metadata(ticker)` with `ON DELETE CASCADE`.
- HNSW index on embedding using `vector_cosine_ops`.
- The `UNIQUE` constraint on `ticker` means only one document per ticker — no multi-document knowledge base.

## External Dependencies

| Dependency | Usage |
|---|---|
| **Ollama** (localhost:11434) | Embedding generation (`/api/embeddings`) and text generation (`/api/generate`) |
| **nomic-embed-text** model | 768-dim embedding model |
| **llama3.2** model | Chat/generation model |
| **PostgreSQL + pgvector** | Vector storage and cosine similarity search |
| **Dapper** | Raw SQL query execution for vector operations |

## Existing Patterns & Conventions

- Interfaces in `EtfInsight.Core.Interfaces`, implementations in `EtfInsight.Infrastructure`.
- DI registration in `Program.cs` with `AddScoped`.
- `IDbConnection` injected as scoped `NpgsqlConnection`.
- Repositories use Dapper with raw SQL, not EF Core (EF Core is used elsewhere in the project).
- No result types — exceptions used for all error paths.
- No `CancellationToken` anywhere in the AI/search stack.

## Potential Issues

1. **Double embedding in `ChatController.Ask()`** (ChatController.cs:55) — The controller generates the question embedding and searches again after `AskAiAsync` already did the same work internally. Two Ollama round-trips and two DB queries per chat request.

2. **No `CancellationToken` propagation** — None of the interfaces (`IEmbeddingGenerator`, `ISemanticSearchRepository`, `IChatService`) accept `CancellationToken`. All Ollama HTTP calls and DB queries run without cancellation support.

3. **Seed data hardcoded in controller** (SemanticSearchController.cs:34-63) — 13 descriptions baked into the controller body. Not extensible, not configurable, mixes data with request handling.

4. **Seed endpoint has no auth or protection** — `POST /api/search/seed` is publicly accessible with no authorization. It overwrites all existing embeddings.

5. **Single document per ticker** — The `UNIQUE` constraint on `ticker` means the knowledge base is limited to one description per instrument. Cannot store multiple documents (factsheets, news, analysis) per ticker.

6. **`OllamaEmbeddingService` sets `BaseAddress` in constructor on a factory-created client** (OllamaEmbeddingService.cs:31-32) — The `IHttpClientFactory` client named "Ollama" has its base address overwritten every time the service is constructed. This is safe only because it's scoped, but it defeats the purpose of named client configuration.

7. **`EmbeddingDimensions` never validated** (AISettings.cs:13) — If a model returning a different dimension count is configured, the `vector(768)` column will reject the insert at DB level with no helpful error message.

8. **Prompt formatting** (OllamaChatService.cs:101-109) — String concatenation with `$"..."` produces no newlines between sections. The `INSTRUCTIONS:` block runs directly into the context, likely degrading LLM output quality.

9. **Broad `catch (Exception)` in controllers** — Both controllers catch `Exception` and return 500, violating the coding standard of catching specific exceptions.

10. **Response DTOs are anonymous types** — Both controllers return `Ok(new { ... })` anonymous objects. No typed response contracts.

11. **`SearchResult` is a mutable class, not a record** (SearchResult.cs) — Properties have no `required` modifier and are not init-only. Violates the coding standard preference for records for DTOs.

## Open Questions

1. Should the seed endpoint be replaced by an automated ingestion pipeline (e.g., from the Airflow `etf_knowledge_builder` DAG)?
2. Is the one-document-per-ticker constraint intentional, or should the schema support multiple documents per ticker for a richer knowledge base?
3. Should the chat service return source documents as part of its response to avoid the double-embedding problem in the controller?
4. Is Italian the intended language for all seed descriptions and suggestions, or should multi-language support be considered?
5. Is there a plan to migrate from the legacy Ollama `/api/embeddings` endpoint to the newer `/api/embed` endpoint?

