# Research: Broker PDF Import — Frontend View and Full Feature Stack

## Overview

The broker PDF import feature allows a user to upload a batch of Trade Republic transaction PDFs for a given portfolio. The backend parses each file asynchronously in a Hangfire background job, resolves the instrument from its ISIN, triggers JIT price ingestion when needed, and inserts a transaction row per file. The frontend provides a dedicated view with a dropzone, an upload action, and a live polling progress UI that auto-refreshes the portfolio analytics on terminal success.

The feature spans all four layers of the stack: PostgreSQL schema, .NET backend (API + infrastructure), Hangfire job processing, and a Vue 3 single-page frontend.

---

## Entry Points

### HTTP

| Method | Route | Source |
|---|---|---|
| `POST` | `/api/portfolios/{id}/import/broker-pdf` | `BrokerPdfImportController.StartImport` |
| `GET` | `/api/import-jobs/{jobId}` | `BrokerPdfImportController.GetJobStatus` |

Both endpoints resolve `userId` from `HttpContext.GetGuestId()`.

### Frontend route

`/portfolios/:id/import/broker-pdf` maps to `BrokerPdfImportView.vue` via the Vue Router definition in `frontend/src/router/index.ts` (line 41–44).

### Hangfire

The upload handler enqueues `IBrokerPdfImportService.ProcessTradeRepublicImportAsync(jobId, userId, CancellationToken.None)` on the `"broker-imports"` queue. Retry is disabled (`[AutomaticRetry(Attempts = 0)]`).

---

## Core Data Flow

### Upload path (synchronous, within HTTP request)

1. `POST /api/portfolios/{portfolioId}/import/broker-pdf` receives a `multipart/form-data` request.
2. Controller validates: at least one file, at most 100 files, each file ≤ 10 MB, extension `.pdf`. Rejects early with 400 on any violation.
3. Controller calls `IBrokerPdfImportService.StartImportAsync(portfolioId, userId, files, ct)`.
4. Service verifies portfolio ownership via `IPortfolioRepository.GetByIdAndUserAsync(portfolioId, userId, ct)`. Returns `not_found` on failure.
5. A domain `jobId` (Guid) is generated. A temp folder `broker-imports/{jobId}/` is created on disk.
6. Each file is streamed to disk. A SHA-256 hash is computed from the saved file.
7. One `BrokerImportJob` row and one `BrokerImportJobItem` row per file are inserted via `IBrokerImportRepository.CreateJobAsync`.
8. Hangfire enqueues `ProcessTradeRepublicImportAsync`. The Hangfire job id is logged but not exposed to the caller.
9. Returns `202 Accepted` with `{ jobId, status: "queued", totalFiles, message }`.

### Processing path (Hangfire background job)

1. `ProcessTradeRepublicImportAsync` marks the job `processing`.
2. For each item in the job:
   a. Set item status to `parsing`.
   b. `PdfPigTextExtractor.ExtractTextAsync` reads the temp file synchronously via PdfPig (wrapped in `Task.Run` to avoid blocking the async call chain). Returns `PdfExtractionResult { Title, RawText }`.
   c. `TradeRepublicTextNormalizer.Normalize` cleans up CRLF, zero-width chars, and whitespace.
   d. `TradeRepublicDocumentKindDetector.Detect` classifies the document from the PDF title and Italian body keywords.
   e. If kind is not `BuyConfirmation`, `SellConfirmation`, or `SavingsPlanExecution`, item is set to `unsupported` and skipped.
   f. `TradeRepublicParser.Parse` runs a set of compiled Regex patterns against the normalized text, returning `TradeRepublicParserResult` (a discriminated union: `Success`, `Failure`, `Unsupported`).
   g. On `Failure` or `Unsupported`, item is marked accordingly and processing continues to the next item.
   h. On `Success`, duplicate detection runs: `IsDocumentAlreadyImportedAsync(portfolioId, fileSha256, brokerReference)` checks `broker_import_job_items` for any prior row matching hash or broker reference in the same portfolio.
   i. If duplicate, item is marked `duplicate` and skipped.
   j. Item is updated with all parsed fields, status set to `parsed`.
   k. `IInstrumentResolutionService.ResolveTickerByIsinAsync(isin, instrumentName)` is called. The registered implementation is `OpenFigInstrumentResolutionService`, which first queries `etf_metadata` by ISIN, then falls back to the OpenFIGI v3 API.
   l. If still null, item is marked `unresolved_instrument` and skipped.
   m. `IIngestionService.EnsureTickerReadyAsync(ticker, isin, instrumentName)` is called to trigger or confirm JIT ingestion.
   n. The transaction is inserted via `InsertBrokerTransactionAsync` (populating source provenance columns: broker, reference, secondary reference, document hash, ISIN, trade currency).
   o. If ingestion status is `Ingesting`, item is set to `waiting_for_ingestion`; otherwise `imported`.
3. After the loop, `UpdateJobCountersAsync` recomputes all counters from actual item statuses.
4. If any items are `waiting_for_ingestion`, the job is set to `waiting_for_ingestion` instead of `completed`.
5. Otherwise, final status is `completed_with_errors` (if any items are `failed` or `unresolved_instrument`) or `completed`.
6. Temp files are deleted unless the job is `waiting_for_ingestion`.

### Polling path (called by frontend on an interval)

1. `GET /api/import-jobs/{jobId}` resolves `userId`, calls `GetJobStatusAsync(jobId, userId)`.
2. Loads the job row and all item rows.
3. For each distinct `resolved_ticker` on items of this job, joins `etf_metadata.status` via `GetTickerStatusesForJobAsync`.
4. If job is `waiting_for_ingestion`: checks whether all `waiting_for_ingestion` items now have a terminal ticker status (`ready` or `error`). If so, transitions them to `imported`, recomputes counters, marks job `completed` or `completed_with_errors`, deletes temp files.
5. Returns `ImportJobStatusResponse` with all counters, recent items (last 10 by `updated_at`), and `tickerIngestionStatuses` (a `Record<string, string>` map of ticker → metadata status).

### Frontend polling loop

1. `submit()` in `BrokerPdfImportView.vue` calls `portfoliosApi.importBrokerPdf`, receives `StartBrokerImportResponse`, passes `jobId` to `useImportJobPolling().start(jobId)`.
2. `useImportJobPolling` immediately polls once, then sets a `setInterval` at 2500 ms.
3. On each poll, `importJobsApi.getStatus(jobId)` calls `GET /api/import-jobs/{jobId}`.
4. Terminal statuses (`completed`, `completed_with_errors`, `failed`) stop the interval.
5. A 404 response also stops polling and surfaces an error.
6. Transient non-404 errors show a "retrying" message but do not stop the interval.
7. When `isTerminalSuccess` becomes true (status is `completed` or `completed_with_errors`), a `watch` in the view triggers `portfoliosStore.fetchPortfolios()` and `portfoliosStore.selectPortfolio(portfolioId)`.

---

## Key Components

### `BrokerPdfImportController.cs`
- Two endpoints: upload and polling.
- Validates file count (≤100), size (≤10 MB), and extension (`.pdf`) before delegating to the service.
- Resolves user identity with `HttpContext.GetGuestId()`.
- Returns 404 when the service signals `not_found`, otherwise 202/200.

### `BrokerPdfImportService.cs`
- Primary orchestrator for both the HTTP-side setup and the Hangfire job.
- `StartImportAsync`: creates temp folder, streams files to disk, computes SHA-256, inserts DB rows, enqueues Hangfire.
- `GetJobStatusAsync`: polling logic including auto-transition from `waiting_for_ingestion` to terminal.
- `ProcessTradeRepublicImportAsync`: full per-item pipeline (extract → normalize → classify → parse → deduplicate → resolve → ingest → insert).
- `CleanupStaleTempFoldersAsync`: public method (presumably registered as a recurring Hangfire job) that deletes temp folders older than 24 hours.
- Uses `with` expressions on `BrokerImportJobItem` records — the entity must be declared as a `record`.

### `DapperBrokerImportRepository.cs`
- All queries use raw SQL via Dapper with explicit column aliases to map snake_case DB columns to PascalCase properties.
- `UpdateJobCountersAsync` derives all counters from actual item statuses using `COUNT(*) FILTER (WHERE status = ...)` — no manual increment, no drift.
- `IsDocumentAlreadyImportedAsync` queries `broker_import_job_items` (not `transactions`) — this means a document that was imported in a different job **for the same portfolio** is still detected if the item row exists, but only if items are preserved after completion (they are never deleted in the current code, only temp files are deleted).
- `InsertBrokerTransactionAsync` converts `DateOnly` to `DateTime` by calling `.ToDateTime(TimeOnly.MinValue)` before passing to Dapper.

### `PdfPigTextExtractor.cs`
- Uses `UglyToad.PdfPig`. Pure managed code, no native dependencies.
- `PdfDocument.Open()` is synchronous; the implementation wraps it in `Task.Run` to avoid blocking the caller.
- Reads PDF title from document metadata (`document.Information.Title`); returns `null` if absent or whitespace.
- Concatenates all pages via `page.Text`, separated by `AppendLine`.

### `TradeRepublicTextNormalizer.cs`
- Normalizes line endings (CRLF → LF), zero-width and non-breaking whitespace, intra-line whitespace runs, and collapses 4+ consecutive blank lines to two.
- Does **not** normalize decimal commas or date formats — these are handled per-field inside the parser.

### `TradeRepublicDocumentKindDetector.cs`
- Checks PDF title (case-insensitive) first, then falls back to Italian body keywords.
- Handles `"Savings Plan Execution"`, `"Order Confirmation"`, `"Securities Settlement"`, and `"Dividend"` title variants.
- `"Securities Settlement"` and `"Order Confirmation"` dispatch to Buy/Sell via `ACQUISTO`/`VENDITA` body keywords.
- Body fallback handles `"PIANO DI ACCUMULO"` (savings plan), `"ACQUISTO"` (buy), `"VENDITA"` (sell), `"DIVIDENDO"` (dividend).
- Does not detect `Tax` or `CashMovement` via keywords in the current implementation — those would fall through to `Unknown`.

### `TradeRepublicParser.cs`
- 9 compiled Regex patterns covering ISIN, execution ref, plan ref, order ref, gross amount, fee, transaction date, settlement date, instrument row, and a flattened fallback instrument pattern.
- `InstrumentRowPattern` uses a strict two-line format (header + data row) requiring EUR literals inline.
- `FlattenedInstrumentPattern` (Singleline, spans multiple lines) is used as a fallback when the structured row match fails; it validates the parsed amount against `grossAmount` and brute-forces unit/price split via `TryResolveUnitsAndPrice`.
- `TryResolveUnitsAndPrice` tries every possible character-level split of a numeric blob and checks whether `units × pricePerUnit` rounds to `grossAmount`. No assumption about decimal place count.
- Fee extraction uses `Supplemento\s+spese\s+di\s+terzi` — Italian-locale specific. The absolute value is stored.
- `brokerReference` is `null` if no `ESECUZIONE` match is found (non-fatal; hash-based dedup still applies).
- Parser normalizes the text again internally (calls `TradeRepublicTextNormalizer.Normalize`) — this is a second normalization pass on already-normalized text (idempotent in practice).
- `DocumentKindDetector.Detect` is also called again inside `Parse` — detector runs twice per item (once in the service before calling the parser, and once inside `Parse` itself).

### `EtfMetadataInstrumentResolutionService.cs`
- Fast path only: single `SELECT ticker FROM etf_metadata WHERE isin = @Isin LIMIT 1`.
- This service is registered as the `IInstrumentResolutionService` implementation only when `OpenFigInstrumentResolutionService` is **not** the registered implementation.

### `OpenFigInstrumentResolutionService.cs`
- DB fast path first, then OpenFIGI v3 API call if no DB match.
- Composes Yahoo Finance-compatible tickers by appending exchange suffixes (`.MI`, `.DE`, `.L`, etc.).
- Preferred exchange order: `IM`, `GR`, `LN`, `EO`, `EP` (Borsa Italiana first).
- No upsert of newly resolved ticker into `etf_metadata` — the resolution result is used only for the JIT ingestion call. If the ticker is resolved via OpenFIGI but not yet in `etf_metadata`, `EnsureTickerReadyAsync` must handle the upsert.
- Falls back to `null` (not an exception) on HTTP error, deserialization error, or no match for preferred exchanges.

### `BrokerPdfImportView.vue`
- Uses `useImportJobPolling()` composable (single instance per view mount).
- Exposes 6 counter cards (Total, Processed, Imported, Duplicates, Failed, Waiting).
- "Pending tickers" section shows tickers whose ingestion status is not `ready` or `error`.
- Recent results section shows up to 10 items (last 10 by `updated_at` from backend).
- Progress bar color changes by status: red for `failed`, amber for `completed_with_errors`, sky for `waiting_for_ingestion`, primary (default) otherwise.
- `effectiveStatus` computed property: uses polling status if available, otherwise `startResponse.status` — prevents status card from being blank before the first poll completes.
- `onFilesSelected` resets polling state, clearing any prior job's data.
- Portfolio refresh on success is guarded by `hasRefreshedPortfolio` flag to prevent double refresh if the watcher fires multiple times.

### `BrokerImportDropzone.vue`
- Client-side filter: only `.pdf` extensions are passed to the parent; non-PDFs are listed in a rejected-files warning.
- Shows up to 8 selected file names; remaining count shown as `+ N more`.
- Does not apply size or count validation — those are enforced in the controller.

### `useImportJobPolling.ts`
- Default interval: 2500 ms.
- Polls once immediately on `start(jobId)` before starting the interval.
- Stops on terminal status (`completed`, `completed_with_errors`, `failed`).
- Stops and surfaces error on 404 response.
- Does not stop on transient errors (non-404), surfacing a retrying message instead.
- `reset()` clears interval, job, error, and `activeJobId`.
- `onUnmounted(stop)` — interval is cleared on component teardown.
- `pendingTickers` filters out `ready` and `error` ticker statuses from `tickerIngestionStatuses`.

---

## External Dependencies

### PostgreSQL
- Tables: `broker_import_jobs`, `broker_import_job_items`, `transactions`, `etf_metadata`, `portfolios`.
- PostgreSQL-native enum cast syntax: `@Status::broker_import_job_status`, `@Status::broker_import_item_status`.
- `broker_import_jobs.started_at` is set by a `CASE WHEN` expression in `UpdateJobStatusAsync`: set to `NOW()` when status first transitions to `"processing"` and `started_at` is still null.
- `UpdateJobCountersAsync` uses `COUNT(*) FILTER (WHERE status = ...)` — PostgreSQL-specific syntax.

### Hangfire
- Queue: `"broker-imports"` — separate from the default queue.
- Worker count: 2 (from `Program.cs`, noted in the plan).
- Retry: 0 (explicit `[AutomaticRetry(Attempts = 0)]`).
- The Hangfire job id is stored internally but not returned to the API caller.
- `CancellationToken.None` is passed to `Enqueue` — Hangfire does not propagate cancellation tokens; this is expected behavior.

### Airflow (JIT ingestion)
- `IIngestionService.EnsureTickerReadyAsync(ticker, isin, instrumentName)` is implemented by `AirflowIngestionService`.
- Airflow triggers the `etf_backfill_jit` DAG for unknown tickers.
- The import feature does not interact with Airflow directly — all JIT calls go through `IIngestionService`.

### PdfPig (`UglyToad.PdfPig`)
- Pure managed NuGet, no native library.
- Synchronous API wrapped in `Task.Run`.

### OpenFIGI API
- `POST https://api.openfigi.com/v3/mapping`
- Optional API key via `config["OpenFigi:ApiKey"]`.
- Named `HttpClient` (`"OpenFigi"`) registered in DI.

---

## Existing Patterns & Conventions

- **Repository pattern with Dapper**: raw SQL, explicit column aliases, no ORM/EF.
- **Discriminated union result type**: `TradeRepublicParserResult` (`Success`, `Failure`, `Unsupported`) — follows the project standard of using result types instead of exceptions for expected failure paths.
- **Records for entities**: `BrokerImportJobItem` is a record (confirmed by use of `with` expressions in the service).
- **Nullability**: `ArgumentNullException.ThrowIfNull` at public method boundaries.
- **Error truncation**: `error_message` capped at 500 chars in `UpdateItemAsync` via the private `Truncate` helper.
- **CancellationToken threading**: passed through the full call chain except into Hangfire (which uses `CancellationToken.None` by design).
- **Guest ownership**: both endpoints resolve user id from `HttpContext.GetGuestId()` and the repository queries always include `user_id = @UserId`. This is consistent and correct, unlike some older controllers in the codebase.
- **Logging structured properties**: all log lines include job id, portfolio id, and item id where relevant.
- **Frontend polling composable**: `useImportJobPolling` follows the same composable pattern as `useIngestionPolling` but is independent — not reusing the ticker-specific store.
- **TypeScript types**: all API shapes are typed in `frontend/src/types/index.ts`. `BrokerImportJobStatus` and `BrokerImportItemStatus` are string literal union types.

---

## Potential Issues

### 1. Duplicate detection queries `broker_import_job_items`, not `transactions`
`IsDocumentAlreadyImportedAsync` checks for prior item rows in `broker_import_job_items`, not in `transactions` directly. If a transaction was inserted from a previous job but the job items have been cleaned up (e.g. via a future cleanup migration or manual DB operation), a second upload of the same PDF would not be detected as a duplicate and a duplicate transaction would be inserted.

The plan notes that the query should check `transactions.source_document_hash` and `source_reference` for cross-job idempotency, but the current implementation does not do this.

### 2. Normalizer and detector called twice per item
`TradeRepublicParser.Parse` internally calls `TradeRepublicTextNormalizer.Normalize` and `TradeRepublicDocumentKindDetector.Detect` again. The service already normalizes the text and detects the kind before calling `Parse`. This is redundant CPU work per PDF and means the kind check in the service (which gates whether `Parse` is called) is repeated inside `Parse`. For typical PDF sizes this is not a performance concern, but it creates a subtle inconsistency risk if the two detection paths ever diverge.

### 3. `UnsupportedFiles` counter is not tracked in job counters
The plan notes that `failed_files` counts only `failed` items, not `unsupported` items. There is no `unsupported_files` counter in `broker_import_jobs`. Unsupported items are silently absorbed into `processed_files` only. The frontend has no way to surface a count of unsupported documents distinctly from failures.

### 4. `BrokerSecondaryReference` is `null` for securities-settlement documents if `ORDINA` regex does not match
The `OrderRefPattern` targets `ORDINA\s*...` but the plan confirms that the securities-settlement body contains `ORDINA d74c-69c3`. If the boundary condition in the pattern does not match exactly (e.g. lookahead fails due to unexpected whitespace), `BrokerSecondaryReference` is silently `null`. This reduces provenance completeness for one of the two confirmed real-world document variants.

### 5. OpenFIGI does not upsert into `etf_metadata`
`OpenFigInstrumentResolutionService.ResolveViaOpenFigiAsync` returns a ticker string but does not write anything to `etf_metadata`. All persistence of the resolved ticker must happen in `EnsureTickerReadyAsync`. If the ingestion service's upsert creates the `etf_metadata` row with a new ticker but a subsequent call to `ResolveTickerByIsinAsync` runs before the row is committed or visible, the resolution will repeat the OpenFIGI call unnecessarily. This is a race condition under concurrent imports for the same ISIN.

### 6. Temp folder creation happens before DB row insertion
In `StartImportAsync`, `Directory.CreateDirectory(jobFolder)` is called before the database insert. If the DB insert fails, the temp folder and its files are left on disk with no corresponding DB row to track them. The 24-hour cleanup job would eventually remove them, but there is no immediate cleanup on failure.

### 7. `started_at` is set by a `CASE WHEN` expression inside `UpdateJobStatusAsync`
This means `started_at` is only set when the status is being set to `"processing"`. If Hangfire retries were enabled (they are currently disabled via `Attempts = 0`), a second Hangfire run would not reset `started_at` because the `CASE WHEN` guards against overwriting a non-null value.

### 8. File size validation in the controller does not check MIME type
The controller checks `.pdf` extension and `f.Length` but does not verify the MIME type (e.g. `application/pdf`). A file with a `.pdf` extension but non-PDF content will pass controller validation, fail during PdfPig extraction, and be marked `failed` with an extraction error. This is not a security concern (PdfPig is sandboxed), but the error message quality differs from a clear "invalid file" rejection at the boundary.

### 9. `GetJobStatusAsync` auto-transition is racy on concurrent polls
The auto-transition from `waiting_for_ingestion` to `completed`/`completed_with_errors` happens inside `GetJobStatusAsync`, which is called on every poll. Multiple concurrent poll calls (e.g. if the user has two browser tabs open) could both read the job as `waiting_for_ingestion`, both conclude all tickers are terminal, and both attempt the same `UpdateItemAsync` + `MarkJobCompletedAsync` writes. There is no database-level lock or optimistic concurrency guard preventing this double-write.

### 10. `BrokerImportDropzone.vue` shows only 8 files but emits all
The component limits the displayed list to `visibleFiles = selectedFiles.slice(0, 8)` but emits the full array to the parent. When a user selects many files, the feedback is truncated to 8 names, but all files are submitted. This is a cosmetic UX limitation, not a functional bug.

---

## Open Questions

1. **Which service implementation is registered for `IInstrumentResolutionService`?** Both `EtfMetadataInstrumentResolutionService` and `OpenFigInstrumentResolutionService` exist and implement the same interface. The `Program.cs` registration determines which is active. If `EtfMetadataInstrumentResolutionService` is registered, any ISIN absent from `etf_metadata` will return null and the item will be marked `unresolved_instrument` — OpenFIGI never called.

2. **Is `CleanupStaleTempFoldersAsync` registered as a recurring Hangfire job?** The method exists on `IBrokerPdfImportService` and is implemented, but it is not clear from the reviewed files whether it is scheduled anywhere (e.g. in `Program.cs` via `RecurringJob.AddOrUpdate`).

3. **Are `broker_import_job_items` rows ever deleted?** Only temp files are deleted. Item rows appear to be permanent. Duplicate detection relies on this permanence. If items are ever pruned (e.g. as part of a future data retention policy), cross-job duplicate detection breaks.

4. **What happens to pending tickers in the ingestion store if the user navigates away during `waiting_for_ingestion`?** The plan mentions optionally registering pending tickers in `useIngestionStore` so the sidebar spinner stays coherent. This is not implemented. The user will see no progress indicator outside the import view if they navigate away.

5. **Is the `OpenFigi:ApiKey` configured in production?** The OpenFIGI free tier is rate-limited. For import batches of 20–100 PDFs with many new ISINs, unauthenticated calls could hit limits silently (returning `null`, resulting in `unresolved_instrument` items) even for valid ISINs.

6. **What is the expected DB schema state?** The research reviewed the service and repository layers but not the actual `09_broker_pdf_import.sql` migration file. The exact column types, enum definitions, and indexes have not been read directly; they are inferred from the repository queries. If schema and code diverge, Dapper will fail at runtime with uninformative column-mapping errors.

