# Research: ETFInsight Platform — Full Technical Deep-Dive

## Overview

ETFInsight is a self-hosted, distributed financial analytics platform that combines quantitative portfolio performance tracking (TWRR, drawdown, PnL), event-driven data pipelines, and local AI-powered question answering (RAG) into a single system. It is designed for long-term retail ETF investors who want a deep, structured understanding of their portfolio without relying on fragmented broker tools.

The system is built with:

- **.NET 9** (Web API + Hangfire background jobs)
- **Vue 3 + TypeScript + Pinia** (SPA frontend)
- **PostgreSQL 16 + pgvector** (relational + vector store)
- **Apache Airflow 2.9** (data engineering orchestration)
- **Ollama** (local LLM inference for RAG and embeddings)
- **Docker Compose** (full-stack container orchestration)
- **Nginx** (reverse proxy, SPA serving, API routing)

The architecture follows Clean Architecture / DDD layering with four .NET projects: `EtfInsight.Api`, `EtfInsight.Core`, `EtfInsight.Infrastructure`, and `EtfInsight.DataQuality`.

---

## Entry Points

### HTTP API (primary entry point)

The .NET 9 API serves all client requests through controllers:

| Controller                  | Route                                      | Purpose                                                      |
| --------------------------- | ------------------------------------------ | ------------------------------------------------------------ |
| `PortfoliosController`      | `/api/portfolios`                          | CRUD portfolios, add transactions, analytics dashboard, TWRR |
| `ChatController`            | `/api/chat`                                | RAG-powered Q&A with local LLM                               |
| `SemanticSearchController`  | `/api/search`                              | Seed and query vector embeddings                             |
| `IngestionController`       | `/api/ingestion`                           | JIT ingestion callback + status polling                      |
| `DataQualityController`     | `/api/data-quality`                        | Enqueue scans, retrieve anomalies                            |
| `CsvImportController`       | `/api/portfolios/{id}/transactions/import` | Bulk CSV transaction import                                  |
| `BrokerPdfImportController` | `/api/portfolios/{id}/import/broker-pdf`   | PDF-based broker statement import                            |
| `HealthCheckController`     | `/health`                                  | Container health probes                                      |

### Airflow DAGs (scheduled/on-demand)

| DAG                   | Schedule                           | Trigger                                 |
| --------------------- | ---------------------------------- | --------------------------------------- |
| `etf_daily_prices`    | `0 22 * * 1-5` (Mon-Fri 22:00 UTC) | Cron                                    |
| `etf_backfill_prices` | Manual                             | Airflow UI/API                          |
| `etf_backfill_jit`    | None (on-demand)                   | .NET API via REST                       |
| `data_quality_scan`   | None (triggered)                   | `etf_daily_prices` completion or manual |

### Hangfire Background Jobs

- `nightly-data-quality-scan`: recurring at 02:00 daily
- `cleanup-stale-broker-import-temp-folders`: recurring at 03:00 daily
- Broker PDF import processing: fire-and-forget per import batch

### Frontend SPA

- Accessed at `:3000` via Nginx
- Routes: `/` (dashboard), `/portfolios`, `/portfolios/new`, `/data-quality`, CSV import, broker PDF import

---

## Core Data Flow

### 1. Portfolio Creation & Transaction Entry

```
User → Vue SPA → POST /api/portfolios (creates portfolio with user_id from X-Guest-ID)
User → Vue SPA → POST /api/portfolios/{id}/transactions
  → Controller validates input
  → IIngestionService.EnsureTickerReadyAsync(ticker)
    → Checks etf_metadata.status
    → If unknown/error: INSERT placeholder → trigger Airflow etf_backfill_jit DAG via REST
    → Returns IngestionStatus (Ready | Ingesting | Error)
  → INSERT transaction (FK satisfied by placeholder row)
  → Returns 201 (ready) or 202 (ingesting)
```

### 2. Just-in-Time (JIT) Price Ingestion

```
.NET API → POST Airflow /api/v1/dags/etf_backfill_jit/dagRuns
  → Airflow DAG: fetch_and_load task
    → yfinance.Ticker(symbol).history(start, end)
    → normalize_prices() + validate_prices()
    → ETFDatabaseHook.upsert_prices() → UPSERT into etf_prices
    → ETFDatabaseHook.upsert_metadata(ticker, "ready")
  → notify_api task
    → POST /api/ingestion/callback with X-Callback-Secret
    → .NET marks etf_metadata.status = 'ready', is_active = true

Frontend polls GET /api/ingestion/{ticker}/status every 3s
  → When "ready": auto-refreshes portfolio analytics
```

### 3. Daily Price ETL

```
Airflow Scheduler → etf_daily_prices DAG (22:00 UTC Mon-Fri)
  → get_active_symbols (reads etf_metadata WHERE is_active = TRUE)
  → TaskGroup: one PythonOperator per symbol
    → yfinance fetch (period=5d for overlap)
  → normalize_and_validate (aggregate all raw → clean)
  → load_prices (ETFDatabaseHook.upsert_prices)
  → trigger_dq_scan (TriggerDagRunOperator → data_quality_scan)
```

### 4. Portfolio Analytics Computation

```
GET /api/portfolios/{id}/analytics/dashboard?from=&to=
  → PortfolioAnalyticsService.GetPortfolioAnalyticsAsync()
    → Load portfolio with all transactions up to 'to'
    → Load prices for all tickers in date range
    → Process transactions BEFORE 'from' to build initial holdings state
    → Day-by-day loop from 'from' to 'to':
      - Apply day's transactions to holdings
      - Compute totalValue = Σ(units × closePrice) using last-known-price fill
      - Track peak, drawdown, PnL, cumulative net flow, simple return
    → Return PortfolioDashboardDto with time-series + summary KPIs
```

### 5. TWRR Calculation

```
GET /api/portfolios/{id}/analytics/summary?from=&to=
  → TwrrCalculator.CalculateTWRR()
    → Day-by-day iteration from first transaction to max price date
    → On each day:
      - valueStart = previous day's end value
      - Process transactions → update holdings + compute cashFlow
      - valueEnd = Σ(units × price)
      - subPeriodReturn = (valueEnd - cashFlow) / valueStart - 1
      - Compound: totalReturn = (1 + totalReturn) × (1 + subPeriodReturn) - 1
    → Returns compounded TWRR as decimal
```

### 6. RAG-Powered AI Chat

```
POST /api/chat { question: "..." }
  → OllamaEmbeddingService.GenerateEmbeddingAsync(question)
    → POST Ollama /api/embeddings with model=nomic-embed-text
    → Returns float[768]
  → DapperSemanticSearchRepository.SearchAsync(embedding, limit=5)
    → SQL: ORDER BY embedding <=> query::vector (cosine distance via pgvector)
    → Returns top-5 matching etf_documents rows
  → OllamaChatService.BuildAugmentedPrompt(question, docs)
    → Constructs context with all matched documents + instructions
  → OllamaChatService.GenerateResponseAsync(augmentedPrompt)
    → POST Ollama /api/generate with model=llama3.2, stream=false
  → Returns answer + sources with similarity scores
```

### 7. Broker PDF Import Pipeline

```
POST /api/portfolios/{id}/import/broker-pdf (multipart/form-data, up to 100 PDFs)
  → Files saved to temp directory with SHA256 hashing
  → broker_import_jobs + broker_import_job_items rows created
  → Hangfire enqueues ProcessTradeRepublicImportAsync on "broker-imports" queue
  → Background job:
    For each PDF:
      → PdfPigTextExtractor: PdfDocument.Open → extract raw text per page
      → TradeRepublicTextNormalizer: strip zero-width chars, collapse whitespace
      → TradeRepublicDocumentKindDetector: classify (Buy/Sell/SavingsPlan/Dividend/Tax/Cash/Unknown)
      → TradeRepublicParser.Parse():
        - Regex extraction: ISIN, instrument row (name/units/price), TOTALE, fees, dates, references
        - Fallback: FlattenedInstrumentPattern for non-standard layouts
        - NumericSplitCandidate brute-force: split concatenated number blob to find units×price = gross
      → Deduplication check (SHA256 + broker_reference)
      → OpenFIGI ISIN → ticker resolution (with exchange suffix mapping)
      → IIngestionService.EnsureTickerReadyAsync() for JIT
      → Insert transaction into DB
  → Final status: completed | completed_with_errors | waiting_for_ingestion
  → Frontend polls GET /api/import-jobs/{jobId} for progress
```

### 8. Data Quality Scanning

```
Hangfire nightly (or triggered by Airflow or manual API call)
  → DataQualityScanner.ScanRecentPricesAsync()
    → Load recent prices from DB
    → For each price row, run all IDataQualityRule implementations:
      - NegativePriceRule: closePrice <= 0 → ERROR
      - FlashCrashRule: |change%| >= threshold (default 20%) → WARNING
    → Persist anomalies to data_anomalies (ON CONFLICT → idempotent)
```

---

## Key Components

### EtfInsight.Core (Domain Layer)

**Entities:**

- `Portfolio`: id (UUID), name, currency (enum: USD/EUR/GBP/JPY), createdAt, transactions list
- `Transaction`: id, portfolioId, ticker, transactionDate (DateOnly), type (BUY/SELL/DEPOSIT/WITHDRAW), pricePerUnit, units, fees
- `Etf`: id, ticker, createdAt
- `EtfPrice`: inherits Etf + priceDate, OHLCV fields, currency
- `BrokerImportJob`: full lifecycle tracking for PDF batch imports
- `BrokerImportJobItem`: per-file state machine with parsed fields

**Services:**

- `TwrrCalculator`: Pure implementation + async repository-backed overload. Day-by-day sub-period compounding with last-known-price forward-fill.
- `PortfolioAnalyticsService`: Orchestrates TWRR + daily valuation history. Handles pre-window state initialization for range queries.

**Interfaces (contracts):**

- `IIngestionService` (EnsureTickerReadyAsync)
- `IPortfolioRepository`, `IEtfPriceRepository`
- `IEmbeddingGenerator`, `ISemanticSearchRepository`
- `IChatService`, `ICsvImportService`
- `IBrokerPdfImportService`, `IBrokerImportRepository`
- `ITradeRepublicParser`, `IPdfTextExtractor`
- `IInstrumentResolutionService`

**DTOs:**

- `PortfolioDashboardDto`: point-in-time KPIs + `DailyValuationPointDto[]` time series
- `CsvImportResult`: imported count, invalid rows, ticker ingestion statuses
- `ParsedTransactionResult`: fully extracted transaction data from broker PDF
- `TradeRepublicParserResult`: discriminated union (Success/Failure/Unsupported)

### EtfInsight.Infrastructure (Data Access + External Services)

**Repositories (all Dapper-based, raw SQL):**

- `DapperPortfolioRepository`: Multi-query with `QueryMultipleAsync`. RLS context via `set_config('app.user_id', ...)`. Bulk insert for CSV.
- `DapperEtfPriceRepository`: `WHERE ticker = ANY(@Tickers)` with date range filtering.
- `DapperSemanticSearchRepository`: pgvector cosine similarity (`1 - (embedding <=> query::vector)`). Embeddings stored as `vector(768)` with HNSW index.
- `DapperDataQualityRepository`: Anomaly persistence with ON CONFLICT idempotency.
- `DapperBrokerImportRepository`: Full job lifecycle CRUD, dedup checking, counter aggregation.

**External Service Integrations:**

- `AirflowIngestionService`: REST API calls to Airflow with Basic auth. Manages etf_metadata lifecycle (placeholder → pending → ingesting → ready/error).
- `OllamaChatService`: RAG pipeline — embed question → semantic search → augmented prompt → generate. Model: llama3.2, temperature: 0.1.
- `OllamaEmbeddingService`: POST /api/embeddings with nomic-embed-text model. Returns 768-dimensional float array.
- `OpenFigInstrumentResolutionService`: ISIN → ticker resolution via OpenFIGI v3 API. Exchange suffix mapping (IM→.MI, GR→.DE, LN→.L, etc.). Preferred exchange ordering.
- `CsvImportService`: CsvHelper parsing with per-row validation, JIT ingestion per distinct ticker, bulk transaction insert.
- `BrokerPdfImportService`: Full Hangfire job orchestration. File SHA256 hashing, temp folder management, progress tracking with `broker_import_jobs` counters.

**Broker PDF Processing Pipeline:**

- `PdfPigTextExtractor`: Uses UglyToad.PdfPig library. Synchronous extraction offloaded via Task.Run.
- `TradeRepublicTextNormalizer`: Strips zero-width chars, normalizes Unicode spaces, collapses multi-blank-lines.
- `TradeRepublicDocumentKindDetector`: Multi-pass classification from PDF title and body keywords (Italian-language: ACQUISTO/VENDITA/PIANO DI ACCUMULO/DIVIDENDO).
- `TradeRepublicParser`: 12+ compiled regex patterns for structured data extraction. Handles standard tabular format and flattened/concatenated layouts. Brute-force numeric blob splitting with validation against gross amount.

### EtfInsight.DataQuality

**Architecture:**

- Strategy pattern: `IDataQualityRule` interface with `ValidateAsync(EtfPrice, EtfPrice?)`
- Rules registered as `AddTransient<IDataQualityRule>` — multiple implementations resolved via `IEnumerable<IDataQualityRule>`
- `DataQualityScanner`: Hangfire-friendly with `[AutomaticRetry(Attempts = 3)]`
- Settings from `DataQualitySettings` (configurable threshold)

**Rules:**

- `NegativePriceRule`: closePrice <= 0 → ERROR severity
- `FlashCrashRule`: |price_change_percent| >= configurable threshold → WARNING severity

### EtfInsight.Api (Application Layer)

**Middleware:**

- `GuestSessionMiddleware`: Reads `X-Guest-ID` header → `HttpContext.Items["GuestUserId"]`. Auto-generates UUID if missing, returns it in response header for client persistence.

**Background Job Infrastructure:**

- Hangfire with PostgreSQL storage
- Worker count: 2
- Queues: "broker-imports" (dedicated), "default"
- Recurring: nightly DQ scan (02:00), stale temp folder cleanup (03:00)

**HTTP Clients (named):**

- "Ollama": LLM inference
- "Airflow": DAG trigger REST API
- "OpenFigi": Instrument resolution

### Frontend (Vue 3 + TypeScript + Pinia)

**Architecture:**

- Vite build system
- Pinia stores: portfolios, ingestion, dataQuality, etfPrices, aiStore
- Composables: `useGuestSession` (localStorage UUID), `useIngestionPolling` (3s interval), `useImportJobPolling` (2.5s interval)
- API layer: axios client with `X-Guest-Id` interceptor + response id sync
- UI: Tailwind CSS + shadcn-vue + ECharts for charts + Lucide icons

**Key Stores:**

- `portfolios`: state management for active portfolio, dashboard data, date range. Auto-fetches dashboard + summary on selection.
- `ingestion`: tracks pending tickers, polls status, auto-refreshes analytics on completion.

**Views:**

- Dashboard (main analytics), Portfolios (CRUD), CSV Import, Broker PDF Import, Data Quality, AI Advisor, Portfolio Create

---

## External Dependencies

### PostgreSQL 16 + pgvector

- Image: `pgvector/pgvector:pg16`
- Extensions: `vector` (for embedding similarity search)
- Tables: `etf_metadata`, `etf_prices`, `portfolios`, `transactions`, `etf_documents`, `data_anomalies`, `etf_prices_audit`, `fx_rates`, `broker_import_jobs`, `broker_import_job_items`
- Custom types: `etf_ingestion_status` enum, `broker_import_job_status` enum, `broker_import_item_status` enum
- RLS: `portfolios_tenant_isolation` policy on `portfolios` table
- Triggers: `trg_audit_prices` → `log_price_changes()` for price update/delete audit trail
- Indexes: B-tree on (ticker, price_date), HNSW on embedding vector

### Apache Airflow 2.9.2

- Executor: LocalExecutor
- Metadata DB: Separate PostgreSQL instance (postgres-airflow)
- Connections: `etf_postgres` (main DB), `etf_api` (HTTP hook for DQ webhook)
- Variables: `etf_static_symbols` (JSON array), `etf_scraper_period`, `dq_webhook_path`
- Python deps: yfinance 1.2.0, pandas 2.2.2
- Custom hook: `ETFDatabaseHook` (extends PostgresHook) with `get_active_symbols()`, `upsert_prices()`, `upsert_metadata()`
- Transform module: `include/transforms/prices.py` with fetch/normalize/validate functions
- Max concurrent JIT runs: 10

### Ollama (local LLM)

- Runs on host machine, accessed at `http://host.docker.internal:11434`
- Embedding model: `nomic-embed-text` (768 dimensions)
- Chat model: `llama3.2`
- Endpoints: `/api/embeddings`, `/api/generate`
- Timeout: 30s (embedding), 60s (chat)

### OpenFIGI API v3

- Endpoint: `https://api.openfigi.com/v3/mapping`
- Used for ISIN → ticker resolution in broker PDF import
- Exchange suffix mapping: IM→.MI, GR→.DE, LN→.L, EO→.AS, EP→.PA, SM→.MC, SW→.SW
- Preferred exchange ordering: IM, GR, LN, EO, EP
- Optional API key via configuration

### Hangfire

- Storage: PostgreSQL (same instance as main DB)
- Queues: "broker-imports", "default"
- Dashboard: `/hangfire` (AllowAll auth filter — dev only)

### yfinance

- Python library for Yahoo Finance market data
- Used in Airflow DAGs for OHLCV fetching
- Methods: `.history(period=)` for daily, `.history(start=, end=)` for backfill

### Nginx

- Serves Vue SPA at `/`
- Proxies `/api/*` to `etf-api:8080`
- Gzip compression for text/css/js
- 1-year cache for static assets
- 1GB max body size (for large PDF uploads)

### Redis

- Present in docker-compose but not actively used by the application layer (reserved for future caching)

---

## Existing Patterns & Conventions

### Architecture

- **Clean Architecture**: Core (domain) has zero dependencies on infrastructure. Infrastructure implements Core interfaces. API wires DI.
- **Repository pattern**: All DB access through interfaces (`IPortfolioRepository`, `IEtfPriceRepository`, etc.)
- **Dapper for data access**: Raw SQL queries, no ORM. `QueryMultipleAsync` for multi-result-set queries.
- **Strategy pattern for rules**: `IDataQualityRule` with multiple implementations resolved via DI collection.
- **Discriminated unions**: `TradeRepublicParserResult` with Success/Failure/Unsupported variants.

### Multi-tenancy

- Guest session ID via `X-Guest-ID` header (client-generated UUID stored in localStorage)
- Middleware propagates to `HttpContext.Items`
- Extension method `HttpContext.GetGuestId()` for clean access
- PostgreSQL RLS on `portfolios` table + app-layer filtering via `set_config('app.user_id', ...)`

### Naming Conventions

- Controllers: PascalCase, `[Controller]` suffix
- Services: PascalCase, `I`-prefix for interfaces
- DB columns: snake_case
- API routes: kebab-case
- TypeScript types: PascalCase interfaces
- Pinia stores: camelCase function names

### Error Handling

- Controllers return typed error objects: `{ Error = "..." }`
- Hangfire jobs: `[AutomaticRetry(Attempts = N)]` decorator
- Airflow DAGs: `on_failure_callback` for metadata status updates
- Non-fatal callbacks: JIT notify_api catches exceptions without failing the DAG

### Ingestion Status State Machine

```
unknown → pending → ingesting → ready
                  ↘ error (retryable: back to pending on next attempt)
```

### Date Handling

- `DateOnly` in C# domain entities (no time component for financial dates)
- Custom Dapper `DateOnlyTypeHandler` for PostgreSQL compatibility
- `DateTime.UtcNow` for audit timestamps

### Configuration

- `appsettings.json` + environment variable overrides in Docker
- Named sections: `AI`, `DataQuality`, `Cors`, `Airflow`, `OpenFigi`, `BrokerImport`
- `IOptions<T>` pattern for typed settings

---

## Database Schema Design

### Core Tables

**etf_metadata** (source of truth for known tickers):

- ticker (UNIQUE, VARCHAR(20)) — FK target for transactions and prices
- isin (VARCHAR(12), nullable)
- status (etf_ingestion_status enum: unknown/pending/ingesting/ready/error)
- is_active (bool) — controls inclusion in daily price fetching
- ingestion_requested_at, ingestion_completed_at, ingestion_error — lifecycle tracking

**etf_prices** (OHLCV time series):

- (ticker, price_date) UNIQUE constraint — enables UPSERT semantics
- Indexes: B-tree on price_date DESC, composite on (ticker, price_date DESC)
- Precision: NUMERIC(18,6)

**portfolios**:

- UUID primary key (gen_random_uuid)
- user_id (UUID, nullable) — multi-tenancy anchor
- RLS policy: `portfolios_tenant_isolation`

**transactions**:

- FK to portfolios (CASCADE delete)
- FK to etf_metadata.ticker (RESTRICT delete) — prevents orphaning
- CHECK constraints on type, units > 0, price_per_unit > 0
- Composite indexes for performance queries

**etf_documents** (vector store):

- embedding: vector(768) with HNSW index (vector_cosine_ops)
- content: TEXT (ETF descriptions for RAG)
- One row per ticker (UNIQUE constraint)

**data_anomalies**:

- Idempotency: UNIQUE (ticker, price_date, rule_name) — safe for re-scans
- Soft-delete pattern: resolved/resolved_at/resolved_by
- Partial index on unresolved rows

**etf_prices_audit** (shadow table):

- Trigger-based: captures UPDATE (price changes) and DELETE operations
- Preserves old_close_price, new_close_price, change_type

**broker_import_jobs / broker_import_job_items**:

- Full state machine with enum types
- Per-file tracking: SHA256, ISIN, resolved ticker, parsed fields
- Counter columns on jobs table for frontend progress display

---

## Potential Issues

### Performance

- `PortfolioAnalyticsService.GetPortfolioAnalyticsAsync()` iterates day-by-day in memory for the entire date range. For portfolios spanning years, this could be slow with many tickers.
- `TwrrCalculator` loads all prices into a dictionary — memory-intensive for large portfolios with long histories.
- Daily valuation uses last-known-price forward-fill, which means weekends/holidays re-use the last available close price rather than skipping days.

### Security

- `AllowAllDashboardAuthorizationFilter` on Hangfire dashboard — must be replaced before production.
- Callback secret (`X-Callback-Secret`) is the only protection for the ingestion callback endpoint — no IP whitelisting.
- No rate limiting on API endpoints.
- Guest session UUIDs are client-generated and unverified — a malicious client could enumerate or impersonate sessions.

### Data Integrity

- `PostgresRepository` (IEtfRepository) has multiple `throw new NotImplementedException()` methods — dead interface.
- `FxRateService` is registered but not used by the analytics engine — cross-currency portfolios will show incorrect valuations.
- The `fk_ticker` constraint on transactions means a transaction cannot be saved for a completely unknown ticker without first creating the etf_metadata placeholder — the JIT flow handles this but there's a race condition window.

### Broker PDF Parsing

- Parser is hardcoded for Italian-language Trade Republic documents only (ACQUISTO, VENDITA, PIANO DI ACCUMULO).
- The `NumericSplitCandidate` brute-force approach for parsing concatenated number blobs is fragile — it tries all possible split positions until `units × price = gross_amount`.
- No OCR fallback — if PdfPig cannot extract text (scanned PDFs), the import silently fails.

### Airflow

- Task graph is static (resolved at parse time from `etf_static_symbols` Variable). New tickers added via JIT are not included in daily fetching until `is_active` is set and the Variable is manually updated or DAG uses `get_active_symbols` at runtime.
- XCom serialization for large price datasets could hit Airflow metadata DB limits.

---

## Open Questions

1. **Currency handling**: `FxRateService` exists with `fx_rates` table but is unused. Are multi-currency portfolios currently calculated in mixed currencies without conversion?

2. **Portfolio valuation project**: The workspace tasks reference `EtfInsight.Portfolio.Valuation` but no such project exists in the repository. Is this planned or removed?

3. **Redis**: Present in docker-compose but no application code uses it. What was the intended use (caching? pub/sub?)?

4. **Factsheet ingestion**: `10_etf_factsheet_status_schema.sql` exists in the DB scripts — what is the intended flow for ETF document/factsheet ingestion?

5. **Authentication roadmap**: The system uses guest sessions only. Is there a planned path to authenticated users, and how would migration of existing guest portfolios work?

6. **Airflow etf_static_symbols vs is_active**: The daily DAG creates tasks at parse time from the Variable, but also calls `get_active_symbols()` from DB. Which is the source of truth — and do they stay in sync?

7. **Chat service improvement** (current branch `feature/79-improve-chat-service-m`): What specific improvements are being made to the RAG pipeline?
