# Research: Chat-Bot End-to-End System

## Overview

The chat-bot is a Retrieval-Augmented Generation (RAG) system that lets users ask natural-language questions about ETFs and their own portfolio. It is composed of two physically separate subsystems:

1. **Offline ingestion pipeline** (Python / Airflow): downloads ETF factsheet PDFs, parses and chunks them, generates vector embeddings via a locally-running Ollama instance, and POSTs them to the .NET API.
2. **Online query pipeline** (.NET API): at request time, embeds the user's question, runs cosine similarity search against the stored chunks, optionally attaches pre-calculated portfolio analytics, builds an augmented prompt, and submits it to Ollama's LLM to generate a grounded answer.

PostgreSQL with the `pgvector` extension (`etf_documents` table) is the shared store between the two pipelines. Ollama serves both the embedding model (`nomic-embed-text`) and the chat model (`llama3.2`) and is expected to run on the Docker host at `host.docker.internal:11434`.

---

## Entry Points

### Ingestion (Airflow)

**DAG `etf_knowledge_builder`** (`airflow/dags/etf_knowledge_builder.py`, scheduled `0 4 * * 0`)
Three tasks in sequence:

1. `get_pending_isins` — queries `etf_metadata` for active ISINs with no `etf_factsheet_status` row or with `failed` status and fewer than 3 attempts.
2. `retrieve_factsheets` — downloads PDFs from DuckDuckGo / JustETF, writes local files, upserts `etf_factsheet_status`.
3. `parse_and_embed` — reads `etf_factsheet_status` rows with `status = 'downloaded'`, parses each PDF, generates per-chunk embeddings via Ollama, POSTs the chunk array to `POST /api/search/ingest` on the .NET API.

### Online API (ASP.NET Core)

| Method | Route                   | Controller                 | Purpose                                                   |
| ------ | ----------------------- | -------------------------- | --------------------------------------------------------- |
| `POST` | `/api/chat`             | `ChatController`           | Ask a question — full RAG pipeline + LLM                  |
| `GET`  | `/api/chat/suggestions` | `ChatController`           | Returns hardcoded example questions (no DB)               |
| `POST` | `/api/search/ingest`    | `IngestController`         | Bulk-replace chunks for a ticker (M2M, API-key protected) |
| `POST` | `/api/search/query`     | `SemanticSearchController` | Raw semantic search without LLM                           |

---

## Core Data Flow

### Offline Ingestion

```
etf_knowledge_builder DAG
  └─ parse_and_embed task
       ├─ ETFDatabaseHook.get_downloaded_factsheets()
       │    └─ SELECT ticker, local_path FROM etf_factsheet_status WHERE status='downloaded'
       └─ for each factsheet:
            ├─ process_factsheet(ticker, pdf_path, ollama_client)   [factsheet_chunker.py]
            │    ├─ extract_text_from_pdf(pdf_path)                 [pdfplumber]
            │    ├─ sliding_window_chunk(text, 2000 chars, 12% overlap)
            │    └─ for each chunk:
            │         └─ generate_embedding(chunk_text, ollama_client)
            │              └─ POST host.docker.internal:11434/api/embeddings
            │                   model=nomic-embed-text
            │                   returns float[] (768 dims)
            └─ POST http://etf-api:8080/api/search/ingest
                 headers: X-API-Key: <INGEST_API_KEY>
                 body: { ticker, chunks: [{ content, embedding, chunkIndex, metadata }] }
```

### Online Chat (`POST /api/chat`)

```
ChatController.Ask
  ├─ GuestSessionMiddleware → resolves userId (Guid) from X-Guest-ID header
  └─ IChatService.AskAiAsync(question, userId, ct)           [OllamaChatService]
       ├─ IEmbeddingGenerator.GenerateEmbeddingAsync(question, ct)  [OllamaEmbeddingService]
       │    └─ POST host.docker.internal:11434/api/embeddings
       ├─ ISemanticSearchRepository.SearchAsync(embedding, limit=MaxContextChunks,
       │         minSimilarity=MinSimilarityThreshold, ct)    [DapperSemanticSearchRepository]
       │    └─ SELECT ticker, content, 1-(embedding<=>query::vector) AS similarity
       │         FROM etf_documents
       │         WHERE similarity >= minSimilarity
       │         ORDER BY embedding<=>query::vector
       │         LIMIT limit
       ├─ BuildPortfolioContextAsync(userId, ct)    [if userId != Guid.Empty]
       │    ├─ IPortfolioRepository.GetAllPortfoliosWithTransactionsAsync(userId)
       │    └─ IPortfolioAnalyticsService.GetPortfolioAnalyticsAsync(portfolioId, -1yr, today)
       │         returns pre-calculated: CurrentTotalValue, TotalInvested, AbsolutePnL,
       │                                 SimpleReturn, MaxDrawdown
       ├─ BuildAugmentedPrompt(question, relevantDocs, portfolioContext)
       │    └─ inline string: system instructions + portfolio snapshot (if present)
       │         + ETF context chunks (with relevance score) + user question
       └─ GenerateResponseAsync(prompt, ct)
            └─ POST host.docker.internal:11434/api/generate
                 model=llama3.2, stream=false, temperature=0.1
                 returns string (trimmed)

       returns ChatResponseDto { Answer, Sources: [SearchResultDto { Ticker, Content, Similarity }] }

ChatController
  └─ serialises to { question, answer, sources[{ ticker, similarity, excerpt(100 chars) }], timestamp }
```

### Ingest (`POST /api/search/ingest`)

```
ApiKeyMiddleware → validates X-API-Key against AISettings.IngestAPIKey
IngestController.Ingest([ApiKeyRequired])
  └─ ISemanticSearchRepository.BulkReplaceAsync(ticker, chunks, ct)
       ├─ opens connection if closed
       ├─ BEGIN TRANSACTION
       ├─ DELETE FROM etf_documents WHERE ticker = @Ticker
       ├─ for each chunk:
       │    └─ INSERT INTO etf_documents (ticker, content, embedding::vector, metadata::jsonb,
       │              is_mandatory=false, chunk_index, source)
       └─ COMMIT (ROLLBACK on any exception)
```

---

## Key Components

### `airflow/dags/etf_knowledge_builder.py`

- Defines the 3-task DAG. Wiring: `get_pending_isins >> retrieve_factsheets >> parse_and_embed`.
- `_parse_and_embed` creates two `httpx.Client` instances per run: one for Ollama (timeout 120s), one for the .NET API (timeout 60s).
- On per-ticker failure (any exception), it logs and continues. The `etf_factsheet_status` row is **not updated** — the status stays `downloaded`. On the next weekly run the file will be retried without incrementing an attempts counter.
- `DOTNET_API_URL` and `INGEST_API_KEY` are read from environment variables; both are set in `docker-compose.yml`.

### `airflow/include/transforms/factsheet_chunker.py`

- `extract_text_from_pdf`: uses `pdfplumber`. Returns empty string if pages have no text (scanned/image PDFs silently produce nothing).
- `sliding_window_chunk`: character-based sliding window, 2000-char chunks, 12% (~240 char) overlap. Terminates the loop correctly with a break once `start + chunk_size >= len(text)`.
- `generate_embedding`: calls Ollama directly at the module-level constant `OLLAMA_URL = "http://host.docker.internal:11434"`. The `client` parameter is used for the HTTP call but the base URL is always the hardcoded constant — it cannot be overridden without changing the source.
- `process_factsheet`: returns a list of dicts with keys `content`, `embedding`, `chunkIndex`, `metadata` (matching the .NET `IngestChunkDto` field names via camelCase JSON serialisation).

### `airflow/plugins/hooks/etf_db_hook.py` — `ETFDatabaseHook`

- Extends Airflow's `PostgresHook` using connection ID `etf_postgres`.
- `get_downloaded_factsheets()`: returns `[{ticker, local_path}]` for all rows with `status='downloaded'`.
- `get_isins_for_factsheet_retrieval()`: selects active ISINs not yet in `etf_factsheet_status` OR with `failed` status and `attempts < 3`.

### `IngestController` (`src/EtfInsight.Api/Controllers/IngestController.cs`)

- Marked `[ApiKeyRequired]`. The `ApiKeyMiddleware` intercepts and validates before the action body runs.
- Validates non-empty `Ticker` and non-empty `Chunks` list explicitly. Delegates entirely to `BulkReplaceAsync`.
- Returns `{ ticker, chucksIngested }` (note: typo `chucksIngested` instead of `chunksIngested` in the response body).

### `ApiKeyMiddleware` (`src/EtfInsight.Api/Middleware/ApiKeyMiddleware.cs`)

- Reads `X-API-Key` header. Compares with `AISettings.IngestAPIKey` using ordinal string equality (timing-safe comparison is not used — see Potential Issues).
- If the key is empty in config (default), any request to a `[ApiKeyRequired]` endpoint is rejected (correct fail-closed behaviour).
- Registered in `Program.cs` via `app.UseMiddleware<ApiKeyMiddleware>()`.

### `OllamaChatService` (`src/EtfInsight.Infrastructure/Services/OllamaChatService.cs`)

- Implements `IChatService`. Scoped DI lifetime.
- Injects `IPortfolioRepository` and `IPortfolioAnalyticsService` to build the portfolio context block. Only the **first** portfolio returned for the user is used; multi-portfolio users silently ignore subsequent portfolios.
- `BuildPortfolioContextAsync`: queries analytics over a rolling 1-year window (`today - 1 year` to `today`). Returns empty string if no portfolio exists or if `CurrentTotalValue == 0`.
- `BuildAugmentedPrompt`: assembles a flat string (no structured chat format). The separator between sections is missing newlines in several places (the format string concatenates with `$"..."` using space-separated literals, losing line breaks between INSTRUCTIONS bullets).
- `GenerateResponseAsync`: `Stream=false`, `Temperature=0.1`. Throws `InvalidOperationException` on empty response.
- Sets `_httpClient.BaseAddress` after obtaining the client from the factory — a known mutation pattern that works but is non-idiomatic.

### `OllamaEmbeddingService` (`src/EtfInsight.Infrastructure/Services/OllamaEmbeddingService.cs`)

- Implements `IEmbeddingGenerator`. Scoped DI lifetime. Timeout 30s (half the chat timeout).
- Throws a descriptive `InvalidOperationException` wrapping `HttpRequestException` for connection failures.
- `OllamaEmbeddingResponse` is `public sealed class` (a DTO for an internal HTTP call unnecessarily surfaced as public).
- `OllamaEmbeddingRequest` is `internal sealed class` (inconsistent with the response type visibility).

### `DapperSemanticSearchRepository` (`src/EtfInsight.Infrastructure/Repositories/DapperSemanticSearchRepository.cs`)

- Implements `ISemanticSearchRepository`. Scoped DI lifetime.
- `SearchAsync`: uses cosine distance operator `<=>`. Similarity = `1 - distance`. Now filters by `minSimilarity` (added in this iteration). `GetEmbeddingString` formats floats using `InvariantCulture` to prevent locale-based comma separators from corrupting the vector literal.
- `BulkReplaceAsync`: synchronous `BeginTransaction()` (Npgsql supports async but Dapper's transaction support here is synchronous). Iterates chunks with individual `INSERT` statements — no batch insert. For large PDFs with many chunks this could be slow.
- `SaveEmbeddingAsync`: legacy method; still present for the original hardcoded seed pathway (now unused by any controller). Inserts with `source = 'manual_seed'` and `chunk_index = 0`.
- `QueryAsync<SearchResult>`: Dapper maps the result set to the `SearchResult` record. As of Dapper 2.1+, init-only properties on records are supported via compiled IL. Older Dapper versions would fail silently or throw.

### `ChatController` (`src/EtfInsight.Api/Controllers/ChatController.cs`)

- Now injects only `IChatService` (the previous duplicate embedding + search call has been removed).
- `userId` is resolved from `HttpContext` via `GetGuestId()` extension which reads the value set by `GuestSessionMiddleware`.
- Error responses include `ex.Message` directly in the `details` field — this leaks internal error messages (stack trace is not included but the message may reveal infrastructure details).

### `SemanticSearchController` (`src/EtfInsight.Api/Controllers/SemanticSearchController.cs`)

- `POST /api/search/query`: uses `_semanticSearchRepository.SearchAsync(queryEmbedding, request.Limit ?? 5, ct: ct)`. `minSimilarity` defaults to `0.65`. `SearchRequest.Limit` has no upper-bound validation — arbitrary values flow to the SQL `LIMIT` clause.
- The original `/api/search/seed` endpoint has been removed in this iteration.

### `GuestSessionMiddleware` (`src/EtfInsight.Api/Middleware/GuestSessionMiddleware.cs`)

- Reads `X-Guest-ID` header. If present and parseable as `Guid`, stores it in `HttpContext.Items`. Otherwise generates a new `Guid`, stores it, and writes it back in the `X-Guest-ID` response header.
- The generated `Guid` is not persisted server-side — it exists only in `HttpContext.Items` for the duration of the request. The client is expected to save it and send it on subsequent requests.
- If a client never sends `X-Guest-ID`, `userId` is always a freshly generated `Guid`, meaning portfolio context is never loaded (since no portfolio can exist for a random ID).

### `AISettings` (`src/EtfInsight.Core/Configuration/AISettings.cs`)

- Bound from the `"AI"` config section.
- Properties: `OllamaUrl` (default `http://localhost:11434`), `EmbeddingModel` (`nomic-embed-text`), `ChatModel` (`llama3.2`), `EmbeddingDimensions` (768), `IngestAPIKey` (empty), `MinSimilarityThreshold` (0.65), `MaxContextChunks` (7).
- `EmbeddingDimensions` is still defined but never consumed anywhere.

### `src/db/11_etf_documents_multi_chunk.sql`

- Drops the `UNIQUE(ticker)` constraint on `etf_documents`.
- Adds `chunk_index INT NOT NULL DEFAULT 0` and `source VARCHAR(50) NOT NULL DEFAULT 'manual seed'` (note: default uses a space, not underscore).
- Adds composite unique constraint `uq_etf_documents_ticker_chuck` (typo: missing 'k', should be `_chunk`).

---

## External Dependencies

### Ollama (HTTP, host.docker.internal:11434)

- **Embedding**: `POST /api/embeddings` with `{ model: "nomic-embed-text", prompt: text }`. Returns `{ embedding: float[] }`. Used by both Airflow (at ingestion time) and the .NET API (at query time).
- **Generation**: `POST /api/generate` with `{ model: "llama3.2", prompt, stream: false, temperature: 0.1 }`. Returns `{ response: string, done: bool }`. Used only by the .NET API at chat time.
- No health check, circuit breaker, or retry in either consumer. Connection refused = exception surfaced to the caller.
- Configured via `AI__OllamaUrl` env var for the .NET API. Hardcoded constant in `factsheet_chunker.py` for Airflow.

### PostgreSQL + pgvector

- Table: `etf_documents` with columns `id` (serial), `ticker` (varchar), `content` (text), `embedding` (vector), `metadata` (jsonb), `is_mandatory` (bool), `chunk_index` (int), `source` (varchar), `created_at` (timestamptz).
- Unique constraint: `(ticker, chunk_index)`.
- Vector search uses `<=>` (cosine distance), provided by the `pgvector` extension.
- Dapper with raw SQL (no EF Core involvement for this feature).

### `IPortfolioRepository` / `IPortfolioAnalyticsService`

- Injected into `OllamaChatService` to build the portfolio context block.
- `GetAllPortfoliosWithTransactionsAsync(userId)` — used to find the first portfolio.
- `GetPortfolioAnalyticsAsync(portfolioId, from, to)` — calculates P&L, returns value, drawdown.

---

## Existing Patterns & Conventions

- AI services (`OllamaChatService`, `OllamaEmbeddingService`) live in `EtfInsight.Infrastructure.Services` and implement interfaces from `EtfInsight.Core`.
- All AI services are `Scoped`.
- Named `HttpClient` `"Ollama"` is used; base address is set on the instance after creation from the factory (not at registration time).
- Repository layer uses Dapper with raw SQL for read-heavy and batch paths; EF Core is used elsewhere in the application for write paths.
- `AISettings` is the single configuration object for all AI/ML settings, bound via `IOptions<AISettings>`.
- DTOs exchanged over HTTP are `sealed record` types with `required init` properties.
- `CancellationToken` is propagated throughout the .NET stack (controllers → services → repositories).

---

## Potential Issues

1. **Timing-unsafe API key comparison** (`ApiKeyMiddleware.cs`, line 24): `string.Equals(providedKey, settings.Value.IngestAPIKey, StringComparison.Ordinal)` is not constant-time. A timing side-channel attack is theoretically possible. Should use `CryptographicOperations.FixedTimeEquals`.

2. **`factsheet_chunker.py` `OLLAMA_URL` is a non-configurable constant** (line 7): The URL is hardcoded at module level. Changing it requires a code edit. There is no environment variable override. If Ollama were to run as a Docker service instead of on the host, this file must be modified.

3. **No ingestion failure status update** (`etf_knowledge_builder.py`, `_parse_and_embed`): On embedding or HTTP failure for a ticker, `etf_factsheet_status.status` remains `downloaded`. The row is not updated to `embed_failed` or similar. On the next weekly run the same file will be re-attempted unconditionally, which is acceptable for retries but makes failure diagnosis harder.

4. **`source` column default mismatch** (`11_etf_documents_multi_chunk.sql`, line 5 vs `DapperSemanticSearchRepository.cs`): The migration sets `DEFAULT 'manual seed'` (space), but `SaveEmbeddingAsync` inserts `'manual_seed'` (underscore). Rows created via the old seed pathway and schema defaults will have inconsistent `source` values.

5. **Constraint name typo** (`11_etf_documents_multi_chunk.sql`, line 8): Constraint is named `uq_etf_documents_ticker_chuck` instead of `uq_etf_documents_ticker_chunk`. Not functionally broken but inconsistent with naming conventions and harder to reference in future migrations.

6. **`BulkReplaceAsync` inserts chunks one-by-one** (`DapperSemanticSearchRepository.cs`): Each chunk is a separate `INSERT` inside the transaction. A 50-page factsheet with ~100 chunks runs 100 individual round-trips. For current data volumes this is acceptable but will degrade with scale.

7. **Only the first portfolio is used in chat context** (`OllamaChatService.cs`, line 112): `portfolios.FirstOrDefault()` silently ignores all portfolios beyond the first for multi-portfolio users.

8. **Prompt string formatting loses line breaks** (`OllamaChatService.cs`, `BuildAugmentedPrompt`): The `$"..."` interpolated string that assembles the INSTRUCTIONS section uses `$" - "` prefixes concatenated without `\n` or `AppendLine`. The resulting prompt is a single long line where instruction bullets run together, which may degrade LLM instruction-following.

9. **`ex.Message` exposed in HTTP 500 response** (`ChatController.cs`, line 76): The `details` field in the error JSON includes `ex.Message`, which may leak infrastructure details (Ollama URL, DB connection strings if they surface in exception messages).

10. **`EmbeddingDimensions` in `AISettings` is still unused**: The property has been present since the first iteration and is still never read. The actual embedding dimension is determined by whatever Ollama returns. A model swap could silently break the vector column schema.

11. **`SearchRequest.Limit` has no upper bound** (`SemanticSearchController.cs`): An unbounded integer flows directly into the SQL `LIMIT` clause.

12. **`OllamaEmbeddingResponse` visibility inconsistency** (`OllamaEmbeddingService.cs`): Response DTO is `public sealed class`; request DTO is `internal sealed class`. Neither should be public.

13. **`IngestResponseDto` is an anonymous type** (`IngestController.cs`, line 44): The response `new { ticker, chucksIngested }` is an anonymous object with a typo (`chucksIngested` instead of `chunksIngested`). This typo is part of the public API contract.

---

## Open Questions

1. **What happens when Ollama is not installed on the host at all?** Both the Airflow `parse_and_embed` task and the .NET `/api/chat` endpoint will fail with `Connection refused`. There is no fallback or graceful degradation message to the end user from the Airflow side.

2. **Should ingestion failures update `etf_factsheet_status`?** Currently a downloaded PDF that cannot be embedded stays in `downloaded` status indefinitely. An `embed_failed` status with an `embed_attempts` counter would make the pipeline observable and allow retry limits.

3. **Is the portfolio context intended only for the first portfolio?** The current implementation silently drops all portfolios beyond the first. If multi-portfolio support is a future requirement, the context builder needs a selection mechanism.

4. **Should `nomic-embed-text` be validated against the vector column dimension at startup?** If the model is changed to one producing a different dimension, the `::vector` cast in SQL will silently truncate or fail at query time.

5. **Is there a plan to run Ollama as a Docker service** (rather than requiring it on the host)? The hardcoded `host.docker.internal` in both `factsheet_chunker.py` and `docker-compose.yml` (`AI__OllamaUrl`) makes it a host dependency that is not tracked in Docker Compose.

6. **Should `/api/search/query` also require authentication?** It is currently unauthenticated. Any caller can run arbitrary vector queries against the document store.
