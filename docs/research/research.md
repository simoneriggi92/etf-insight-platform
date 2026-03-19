# ETF Insight Research Report

Last updated: 2026-03-19

## Scope and Method

I reviewed the repository end-to-end with emphasis on the runtime entry points and the places where behavior is actually implemented:

- root and docs (`README.md`, `docs/README.md`, `docs/vision.md`, `docs/valuation-model.md`, `docs/airflow/plan.md`, `docs/jit ingestion/plan.md`)
- infrastructure (`infra/docker-compose.yml`, Dockerfiles, nginx config)
- database schema and supporting SQL/markdown under `src/db`
- backend projects under `src/EtfInsight.Api`, `src/EtfInsight.Core`, `src/EtfInsight.Infrastructure`, `src/EtfInsight.DataQuality`
- Airflow DAGs, hooks, transforms, and tests under `airflow`
- frontend app under `frontend/src`
- .NET unit tests under `tests/EtfInsight.Tests`

I also ran a light verification pass:

- `dotnet test tests/EtfInsight.Tests/EtfInsight.Tests.csproj`
- `npm run build` in `frontend`
- attempted `dotnet build src/EtfInsight.Api/EtfInsight.Api.csproj`

The report below distinguishes between confirmed implemented behavior and architectural intent that exists only in docs or partial scaffolding.

## Executive Summary

ETF Insight is a multi-part portfolio analytics platform built around four real subsystems:

1. a .NET 9 API for portfolios, analytics, ingestion coordination, chat, and anomaly endpoints
2. a PostgreSQL schema for market data, portfolios, vector search, anomaly storage, and ingestion state
3. Apache Airflow DAGs for scheduled price ingestion, backfills, and JIT single-ticker ingestion
4. a Vue 3 SPA that drives portfolio creation, transaction entry, CSV import, anomaly review, and AI chat

The strongest implemented flows are:

- guest-session portfolio creation and portfolio listing
- JIT ticker ingestion triggered from the API and surfaced in the frontend
- daily valuation, simple return, drawdown, and TWRR calculations
- anomaly scanning using Hangfire plus rule-based data quality checks
- local RAG over seeded ETF descriptions using Ollama + pgvector

The repository also shows clear signs of evolution from a V1 system to a V2 JIT/multi-tenant system. That evolution is only partially complete. Several layers still contain legacy code, stale docs, dead endpoints, or unfinished abstractions.

The most important findings are:

- cash-flow modeling is internally inconsistent for `DEPOSIT` and `WITHDRAW`
- tenant isolation is only partially enforced
- scheduled Airflow updates remain tied to a static symbol list, which weakens the "ingest any ticker" promise after the first JIT load
- the AI assistant is not portfolio-aware even though the UI and docs imply that it is
- multi-currency support exists mostly as schema/docs/UI intent, not as implemented runtime behavior
- tests and build wiring are out of sync with the current codebase

## What the Platform Actually Does

At a product level, the platform is trying to answer three questions:

- what does the user own and how has it performed over time
- is the underlying price data trustworthy
- can the system explain ETFs in natural language using local AI

The current implementation supports:

- portfolios with transactions
- dashboard analytics based on historical prices
- automatic price ingestion for unknown tickers
- CSV bulk import of transactions
- anomaly detection on price series
- vector search and AI chat over ETF descriptions

It does not yet fully support:

- authenticated user accounts
- true portfolio-aware AI answers
- reliable scheduled maintenance of all newly ingested symbols
- full multi-currency valuation and conversion
- comprehensive automated test coverage across the full stack

## Architecture Map

### 1. Backend project split

The repository follows a mostly clean split across four .NET projects:

- `src/EtfInsight.Core`
  - domain entities
  - DTOs
  - service interfaces
  - `TwrrCalculator`
  - `PortfolioAnalyticsService`
- `src/EtfInsight.Infrastructure`
  - Dapper repositories
  - Airflow ingestion orchestration
  - CSV import service
  - Ollama embedding and chat integrations
- `src/EtfInsight.Api`
  - DI wiring in `Program.cs`
  - controllers
  - guest session middleware
  - FX rate service
- `src/EtfInsight.DataQuality`
  - anomaly entities
  - rules
  - scanner
  - rule settings

This split is conceptually sound. The main impurity is that some controllers still query `IDbConnection` directly instead of staying inside repository/service boundaries.

### 2. Runtime infrastructure

`infra/docker-compose.yml` defines the operational environment:

- main PostgreSQL instance using `pgvector/pgvector:pg16`
- .NET API container
- Vue frontend served by Nginx
- separate PostgreSQL for Airflow metadata
- Airflow init, webserver, and scheduler services
- Redis service

Important specifics:

- the frontend proxies `/api/*` to the API container through Nginx
- Airflow talks directly to the main Postgres and the API callback endpoint
- the API talks to Airflow through its REST API
- Ollama is expected to run on the host, not as a compose service

Redis is present in compose but not used by the current application code.

### 3. Request and background execution layers

There are three scheduling/execution mechanisms in play:

- normal HTTP request/response through ASP.NET controllers
- Hangfire background jobs in the API
- Airflow DAGs for ingestion pipelines

That means the system is not purely request-driven. Some important state transitions only happen asynchronously:

- JIT ticker completion
- daily price refreshes
- anomaly scanning

## Database and Data Model

The main schema is defined through SQL files in `src/db`.

### Core tables

- `etf_metadata`
  - master instrument/ticker table
  - seeded with demo ETFs and some equities
  - later extended with ingestion lifecycle columns and status enum
- `etf_prices`
  - OHLCV market data
  - unique on `(ticker, price_date)`
- `portfolios`
  - user portfolio metadata
  - includes `user_id` for guest-session tenancy
- `transactions`
  - portfolio transactions
  - foreign-keyed to `portfolios` and `etf_metadata`
- `etf_documents`
  - one vectorized text document per ticker
  - uses `vector(768)` and HNSW index
- `data_anomalies`
  - anomaly log with idempotency constraint on `(ticker, price_date, rule_name)`
- `etf_prices_audit`
  - shadow audit table for price change history

### Ingestion lifecycle model

`08_etf_ingestion_status.sql` adds:

- enum `etf_ingestion_status`
  - `unknown`
  - `pending`
  - `ingesting`
  - `ready`
  - `error`
- timestamps for request/completion
- error storage

This table is the shared coordination point between:

- API transaction submission
- Airflow JIT ingestion
- frontend polling

### Multi-tenancy model

`07_multi_tenancy.sql` adds:

- `user_id UUID` on `portfolios`
- RLS on `portfolios`
- a policy based on `current_setting('app.user_id')`

This is a guest-session tenancy model, not a real auth/account model.

### Currency model

There are signs of planned multi-currency support:

- `Portfolio.Currency`
- `FxRateService`
- `src/db/fx_schema.md`
- `src/db/add_transaction_currency.md`

But this is not actually implemented end-to-end:

- `fx_rates` exists only as markdown, not as an executed SQL migration file
- transaction currency is not part of the live `transactions` schema file
- the frontend always formats values in USD
- analytics services do not use `FxRateService`

Conclusion: currency support is mostly scaffolding right now.

## Main Runtime Flows

### 1. Portfolio creation and guest sessions

Guest sessions are implemented across frontend and backend:

- frontend generates and stores a UUID in `localStorage` via `useGuestSession.ts`
- Axios attaches it as `X-Guest-Id`
- `GuestSessionMiddleware` reads the header or generates a new one
- `DapperPortfolioRepository` calls `set_config('app.user_id', ...)` before queries

This is a lightweight anonymous multi-tenant model. It works for the read paths that go through `DapperPortfolioRepository`.

### 2. Portfolio analytics flow

The implemented analytics path is:

1. frontend fetches portfolios and selects an active one
2. frontend calls `/api/portfolios/{id}/analytics/dashboard`
3. `PortfolioAnalyticsService` loads the portfolio and historical prices
4. it computes:
   - total value
   - cumulative net flow
   - PnL
   - simple return
   - peak
   - drawdown
   - history time series
5. frontend renders:
   - KPI row
   - portfolio value chart
   - drawdown chart
   - allocation pie chart
   - transaction table

TWRR is calculated separately through `TwrrCalculator` and exposed through `/analytics/summary`.

Important implementation detail:

- `PortfolioAnalyticsService` computes a dashboard time series and simple return
- `TwrrCalculator` computes TWRR for the summary endpoint

So there are two analytics models in parallel, not one unified one.

### 3. JIT ingestion flow

This is the most distinctive V2 feature and is genuinely implemented.

When a transaction uses an unknown ticker:

1. `PortfoliosController.AddTransaction` calls `IIngestionService.EnsureTickerReadyAsync`
2. `AirflowIngestionService` checks `etf_metadata.status`
3. if needed, it inserts or updates a placeholder `etf_metadata` row
4. it calls Airflow REST API to trigger `etf_backfill_jit`
5. on success, API marks the ticker as `ingesting`
6. transaction insert proceeds immediately because the metadata FK is now satisfied
7. API returns `202 Accepted` when ingestion is still in progress
8. frontend registers the ticker in `useIngestionStore`
9. store polls `/api/ingestion/{ticker}/status`
10. Airflow fetches historical prices, writes them to `etf_prices`, updates metadata, and best-effort posts back to the API
11. when the frontend sees `ready`, it refreshes portfolio analytics

This is the most coherent end-to-end pipeline in the codebase.

Important specifics:

- the API intentionally saves the transaction before prices are available
- `etf_metadata` acts as both FK anchor and ingestion status tracker
- Airflow updates DB state directly and also sends a callback, which is "belt and suspenders"
- CSV import reuses the same JIT mechanism for each distinct ticker

### 4. CSV import flow

CSV import is implemented in a practical way:

- route: `POST /api/portfolios/{portfolioId}/transactions/import`
- file format:
  - `ticker`
  - `transaction_date`
  - `type`
  - `units`
  - `price_per_unit`
  - `fees`

Behavior:

- `CsvImportService` parses with `CsvHelper`
- bad rows are collected and returned, not fatal
- distinct tickers are checked sequentially through `EnsureTickerReadyAsync`
- valid transactions are bulk-inserted through the repository
- response is:
  - `200` if all tickers are ready
  - `202` if some are still ingesting

The sequential ingestion check is intentional because the service uses a single `IDbConnection`.

### 5. Data quality flow

Data quality is implemented with a combination of Hangfire and rule objects:

- rules:
  - `NegativePriceRule`
  - `FlashCrashRule`
- scanner:
  - `DataQualityScanner`
- storage:
  - `data_anomalies`

Trigger paths:

- recurring Hangfire job added in `Program.cs`
- manual API trigger through `POST /api/data-quality/scan`
- Airflow DAG `data_quality_scan`, which calls the API and lets Hangfire do the real work

Important specifics:

- anomalies are idempotent because of the unique constraint
- scan logic looks at recent price rows and compares them to previous prices
- `FlashCrashRule` uses absolute percentage change, so it will also flag large upward moves, not only crashes

### 6. AI / RAG flow

The AI layer is simpler than the UI wording suggests.

Implemented flow:

1. `/api/search/seed` generates embeddings for a hard-coded dictionary of ticker descriptions
2. embeddings are stored in `etf_documents`
3. `/api/chat`:
   - embeds the user question
   - retrieves similar documents by vector distance
   - builds an augmented prompt
   - calls Ollama `/api/generate`
4. frontend floating AI panel sends the question and displays returned sources

Important specifics:

- this is local AI via Ollama
- `etf_documents` is unique on `ticker`, so there is effectively one stored document per ticker
- the seeded content is hard-coded and mostly descriptive text, not documents fetched from issuers
- the chat backend has no portfolio context, no holdings context, and no active portfolio id

Conclusion: the AI subsystem is an ETF-description knowledge base, not yet a portfolio-aware assistant.

## Frontend Findings

The frontend is a straightforward Vue 3 + Pinia + Vite SPA with clean store boundaries.

### What is implemented well

- clear store split for portfolios, ingestion, anomalies, and AI
- simple, understandable routing
- effective polling UX for ingestion
- dashboard charts via ECharts
- CSV dropzone with client preview
- floating AI assistant panel

### Important frontend specifics

- `AppSidebar` shows a global ingestion spinner when any ticker is pending
- `useIngestionStore` refreshes analytics when the last tracked ticker reaches `ready`
- `PortfolioCreateView` exists and supports choosing base currency
- route `/portfolios/:id` reuses `PortfoliosView.vue`

### Frontend limitations and drift

- `PortfoliosView.vue` does not use the route param, so direct navigation to `/portfolios/:id` does not explicitly select that portfolio
- the AI panel suggests portfolio-performance questions, but the backend cannot answer them with actual portfolio data
- charts and tables format values as USD regardless of portfolio currency
- `useIngestionPolling.ts` exists but is not used
- `frontend/src/api/etfPrices.ts` and `useEtfPricesStore` target endpoints that do not exist in the current API

## Airflow Findings

Airflow is used for three jobs:

- `etf_daily_prices`
- `etf_backfill_prices`
- `etf_backfill_jit`

### What is good

- the JIT DAG is compact and purpose-built
- the transform helpers are simple and testable
- backfill and daily DAGs both end by triggering data quality
- price upserts are idempotent on `(ticker, price_date)`

### Most important Airflow-specific limitation

The scheduled and manual multi-symbol DAGs still build fetch tasks from a static parse-time symbol list (`_SYMBOLS`) loaded from an Airflow Variable.

That has a major consequence:

- JIT can ingest any new ticker once
- but `etf_daily_prices` and `etf_backfill_prices` will only create fetch tasks for symbols present in that Airflow Variable when the DAG was parsed
- new tickers activated through JIT are not automatically guaranteed to receive future daily refreshes unless the variable is updated and the DAG graph is rebuilt

This is the single biggest mismatch between the JIT architecture story and the scheduled-ingestion implementation.

### Airflow testing gap

`airflow/tests/test_dag_integrity.py` checks:

- `etf_daily_prices`
- `etf_backfill_prices`
- `data_quality_scan`

It does not check `etf_backfill_jit`, which is the most important V2 DAG.

## What Is Fully Real vs Partial vs Stale

### Fully real enough to rely on

- guest-session-based portfolio creation and listing
- dashboard analytics and TWRR endpoints
- JIT ingestion contract between API, Airflow, and frontend polling
- CSV bulk import with partial-row validation
- anomaly persistence and retrieval
- Ollama embedding/chat integration for seeded ETF descriptions
- Dockerized local stack with Airflow and Postgres

### Partial or scaffolded

- multi-currency support
- full tenant isolation
- portfolio-aware AI
- comprehensive ETF price API/repository layer
- automated testing around API, JIT flow, and frontend behavior

### Stale or legacy

- `src/EtfInsight.Api/README.md`
- `src/db/schema.md`
- the solution file path for the API project
- old price/ticker endpoint assumptions
- `PostgresRepository`
- `Class1.cs`
- several docs that still describe earlier versions of the platform

## Key Findings and Risks

### 1. Cash-flow semantics are inconsistent and currently incorrect for deposits/withdrawals

This is the most serious domain-model issue.

Observed behavior:

- `TwrrCalculator` treats `DEPOSIT` and `WITHDRAW` cash flow as `PricePerUnit`
- `PortfolioAnalyticsService` treats `DEPOSIT` and `WITHDRAW` cash flow as `Units`
- seed data stores deposit amount in `price_per_unit` and uses `units = 1`

Impact:

- dashboard `TotalInvested`, `PnL`, and simple-return values can be materially wrong whenever deposits/withdrawals are used
- TWRR and dashboard totals are not using the same cash-flow interpretation
- the transaction schema is overloaded to represent both security trades and cash movements without a dedicated amount field

This needs a model-level fix, not just a UI tweak.

### 2. Tenant isolation is incomplete and can be bypassed in important paths

The repository-based read paths are tenant-aware, but several controller paths use raw SQL directly and do not consistently enforce ownership.

Important examples:

- analytics endpoints call `PortfolioAnalyticsService`, which loads portfolios with default `Guid.Empty` and explicitly bypasses the `user_id` filter
- transaction insertion checks portfolio existence via raw SQL without ownership filtering
- CSV import checks portfolio existence via raw SQL without ownership filtering
- `transactions` table itself has no RLS policy

Impact:

- knowing another portfolio UUID may be enough to read analytics or write transactions across guest boundaries

The current tenancy model is better described as partial isolation, not strong isolation.

### 3. Daily updates for newly JIT-ingested symbols are not guaranteed

As described in the Airflow section, the multi-symbol DAGs are still anchored to a static symbol graph.

Impact:

- a ticker can be ingested on demand today
- but future scheduled refreshes may silently skip it unless Airflow configuration is manually updated

This undermines the long-term maintenance story for user-added tickers.

### 4. The AI assistant is not actually portfolio-aware

The frontend presents the AI panel as if it can explain:

- portfolio performance
- max drawdown
- allocation advice

But the backend only searches `etf_documents`.

Impact:

- questions about the active portfolio are likely to produce generic ETF-description answers or "not enough information"
- the active portfolio name shown in the panel header is cosmetic, not functional

### 5. Multi-currency is mostly unimplemented

Evidence:

- portfolio creation allows choosing a base currency
- `FxRateService` exists
- FX schema docs exist
- transaction payload includes a currency field

But:

- the live transaction table does not store transaction currency in the main schema
- `FxRateService` is registered but unused
- portfolio analytics are not converted
- frontend formatting is hardcoded to USD

Impact:

- non-USD portfolios will look supported in the UI while not actually being valued correctly

### 6. Legacy code and docs create real maintenance drag

Examples:

- solution file points API to `src/api/EtfInsight.Api` instead of `src/EtfInsight.Api`
- solution file also omits the DataQuality project
- `PostgresRepository` contains multiple `NotImplementedException`s
- old API README documents endpoints that do not exist anymore
- `PortfoliosController.GetTransactions` uses an `int` route parameter and old column names that do not match the live schema
- `frontend/src/api/etfPrices.ts` assumes endpoints that are not implemented

Impact:

- new contributors can easily infer the wrong architecture
- build and test setup confidence drops
- dead code obscures the real execution paths

### 7. Test/build health is currently weak

Confirmed during verification:

- `dotnet test tests/EtfInsight.Tests/EtfInsight.Tests.csproj` fails to compile because the mock repository in tests no longer implements the current `IPortfolioRepository` interface
- `npm run build` fails in this shell because the installed Node version is `16.16.0` while current Vite requires Node `20.19+` or `22.12+`
- API build verification was inconclusive in this environment because `dotnet build src/EtfInsight.Api/EtfInsight.Api.csproj` did not return within repeated waits

Additional gaps from inspection:

- Airflow integrity tests do not cover the JIT DAG
- there are no obvious API integration tests for the JIT flow
- there are no frontend tests

### 8. Some configuration knobs are present but not actually active

Examples:

- `DataQualitySettings.EnableAutoScan` is configured but not used to gate the recurring Hangfire job
- `ScanIntervalMinutes` exists in config, but the model property is `ScanIntervalInMinutes` and neither is used operationally
- Redis is provisioned but unused

These are signs of planned extensibility, but not current behavior.

## Notable Implementation Specificities

These are not necessarily bugs, but they are important to understand the repo correctly.

- `etf_documents` is effectively one document per ticker because of a unique constraint on `ticker`
- semantic search seed content is manually hard-coded in the controller, not loaded from PDFs or issuer documents
- Airflow JIT callback failure is treated as non-fatal because DB writes already succeeded
- the anomaly scanner stores serialized metadata JSON per anomaly
- allocation is computed from transaction cost basis (`BUY` minus `SELL`) in the frontend, not from latest market value
- the dashboard and TWRR summary are derived from different code paths and formulas
- seed data includes both ETFs and single-name equities, so despite the name, the platform already behaves as a broader portfolio tracker

## Verification Results

### Commands run

- `dotnet test tests/EtfInsight.Tests/EtfInsight.Tests.csproj`
- `npm run build` in `frontend`
- attempted `dotnet build src/EtfInsight.Api/EtfInsight.Api.csproj`

### Outcomes

- .NET tests: failed at compile time
  - cause: stale mocks vs current `IPortfolioRepository`
  - also emitted nullable warnings from `SearchResult`
- frontend build: failed due environment version mismatch
  - shell Node version: `16.16.0`
  - required by Vite: `20.19+` or `22.12+`
- API build: not conclusively verified in this environment

## Recommended Next Priorities

If this repository is going to continue evolving, the highest-value next fixes are:

1. normalize the transaction/cash-flow model
   - introduce a proper cash amount representation for `DEPOSIT` and `WITHDRAW`
   - align `TwrrCalculator`, `PortfolioAnalyticsService`, validation, and UI
2. close tenant-isolation gaps
   - enforce ownership checks on analytics, transaction insertion, and CSV import
   - consider RLS for `transactions` as well
3. fix scheduled maintenance of JIT-added symbols
   - remove the static DAG task graph dependency on a manually maintained Airflow Variable
4. decide what the AI feature truly is
   - either market it as ETF semantic search, or make it portfolio-aware for real
5. remove or complete stale interfaces and docs
   - especially `PostgresRepository`, API README, solution file, and dead endpoints
6. restore build/test confidence
   - repair the .NET test project
   - align the local frontend runtime with the required Node version
   - add at least one end-to-end JIT ingestion test

## Final Assessment

This is a serious and thoughtful personal platform project with real architectural ambition. The JIT ingestion pipeline, Airflow integration, guest-session tenancy concept, and local RAG integration are all meaningful and non-trivial. The repository is not toy-quality.

At the same time, it is best understood as a codebase in active transition:

- the V2 ideas are real
- several of them are already working
- but the financial model, tenancy enforcement, scheduled-ingestion follow-through, and repo hygiene still need consolidation

The most accurate short description is:

> a functioning ETF/equity portfolio analytics platform with JIT price ingestion, anomaly monitoring, and local semantic ETF chat, currently midway between "working prototype" and "hardened product"
