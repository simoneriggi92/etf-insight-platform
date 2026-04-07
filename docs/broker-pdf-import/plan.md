# Broker PDF Import — Architecture & Implementation Plan

Last updated: 2026-03-23

## Goal

Allow a user to upload 20 to 100 Trade Republic transaction PDFs in one action, process them asynchronously, resolve the referenced instruments, trigger on-demand price ingestion for anything missing, and refresh the portfolio once the batch is fully usable.

This plan is based on the current repository structure and the runtime behavior already implemented in:

- `src/EtfInsight.Api/Controllers/CsvImportController.cs`
- `src/EtfInsight.Infrastructure/Services/CsvImportService.cs`
- `src/EtfInsight.Infrastructure/Services/AirflowIngestionService.cs`
- `src/EtfInsight.Api/Controllers/IngestionController.cs`
- `src/EtfInsight.Api/Program.cs`
- `frontend/src/components/portfolios/CsvImportDropzone.vue`
- `frontend/src/stores/ingestion.ts`
- `frontend/src/api/portfolios.ts`

It intentionally reuses the existing .NET API + Hangfire + Airflow + Vue patterns instead of introducing a second architecture.

## Table of Contents

1. Why this feature fits the current platform
2. Current codebase baseline
3. Main gaps specific to Trade Republic PDFs
4. Target end-to-end flow
5. Core design decisions
6. Phase 0 — Discovery and scope lock
7. Phase 1 — Database foundations
8. Phase 2 — API endpoints and job orchestration
9. Phase 3 — PDF extraction and Trade Republic parsing
10. Phase 4 — Instrument resolution and JIT ingestion reuse
11. Phase 5 — Frontend UX
12. Phase 6 — Limits, cleanup, and observability
13. Testing plan
14. Implementation sequence
15. Todo list

## 1. Why this feature fits the current platform

The platform already has three building blocks that make broker PDF import a natural next step:

- bulk import already exists for CSV, so the portfolio domain already accepts many transactions in one operation
- JIT ingestion already exists for unknown instruments, so the system can fetch missing price history on demand
- Hangfire is already configured in `Program.cs`, so long-running import work does not need to stay inside the HTTP request

The broker-PDF feature should therefore be implemented as:

- a new upload entrypoint in the API
- a new Hangfire-backed import job
- reuse of the existing Airflow JIT path for resolved tickers
- a frontend polling experience similar to the current ingestion polling pattern

## 2. Current codebase baseline

### What can be reused directly

- `AirflowIngestionService` already knows how to upsert placeholder metadata and trigger `etf_backfill_jit`
- `IngestionController` already exposes ticker status backed by `etf_metadata.status`
- Hangfire is already running inside the API process
- `CsvImportDropzone.vue` already shows the general drag-and-drop import pattern
- `useIngestionStore` already proves the frontend can track asynchronous ingestion and refresh the active portfolio

### What the provided sample confirms

The provided sample PDF `/Users/simone/Downloads/pb1772470152891748018757362691.pdf` is useful because it confirms several assumptions with an actual Trade Republic document instead of a hypothetical one.

Confirmed from this sample:

- it is a native digital PDF with a readable text layer, so V1 does not need OCR for this document type
- it is a one-page savings plan execution dated 2026-03-02
- the PDF metadata title is `Savings Plan Execution`, while the visible body text is in Italian
- the document exposes `ESECUZIONE 100c-2d49`, which looks like a good primary broker execution reference
- the document also exposes `PIANO DI ACCUMULO c57a-f6c2`, which looks like a recurring plan reference, not a unique transaction id
- the instrument name is present: `Core MSCI World USD (Acc)`
- the ISIN is present: `IE00B4L5Y983`
- the quantity is present with 6 decimals: `7,378349`
- the average price is present with 4 decimals: `113,8466 EUR`
- the gross amount is present: `840,00 EUR`
- a settlement or value date is present separately: `2026-03-04`

Not present in this sample:

- no ticker
- no explicit fee line

This means the plan must treat `ISIN -> ticker` resolution as mandatory, must use execution date rather than value date for the portfolio transaction date, and must increase transaction quantity precision beyond the current `NUMERIC(18,4)`.

### What is not sufficient as-is

- current CSV import is synchronous in-request and would not scale cleanly to 50 to 100 PDFs
- current JIT ingestion is keyed by `ticker`, while Trade Republic PDFs are expected to be ISIN-first
- current `transactions` schema does not preserve broker provenance
- current `transactions` schema does not persist transaction currency
- current `transactions.units NUMERIC(18,4)` is not precise enough for the sample quantity `7,378349`
- current controllers often do raw portfolio existence checks without guest ownership filtering; the new feature should not copy that pattern

## 3. Main gaps specific to Trade Republic PDFs

### 3.1 ISIN-first import does not match the current ticker-first ingestion path

Today the platform can only trigger Airflow with a ticker. The existing JIT flow cannot do:

- `unknown ISIN -> fetch from Airflow`

until the system first resolves:

- `ISIN -> ticker`

The provided sample makes this non-negotiable because it contains an ISIN and product name, but no ticker.

This is the biggest architectural gap for this feature.

### 3.2 The current transaction model is narrower than broker documents

The live transaction model supports:

- `BUY`
- `SELL`
- `DEPOSIT`
- `WITHDRAW`

Trade Republic PDFs may include more document types:

- normal buy/sell confirmations
- savings plan executions
- dividends
- taxes
- fee-only documents
- cash movements

V1 should not attempt to map every broker document to the current model. It should explicitly support only the subset that the live schema and analytics can represent safely.

The provided sample is a savings plan execution and should be treated in V1 as a supported `BUY`-equivalent document type.

### 3.3 The current schema does not support idempotent document import

If a user uploads the same PDF twice, the current database has no broker-specific uniqueness or provenance fields to stop duplicate transaction insertion.

### 3.4 Large multi-file upload needs infrastructure limits to be raised

The current frontend Nginx config does not set `client_max_body_size`. In practice, a 20 to 100 PDF upload can hit reverse-proxy limits before the request ever reaches ASP.NET.

### 3.5 Current transaction precision is too low for broker-originated fractional units

The current live schema in `src/db/03_portfolio_schema.sql` stores:

- `units NUMERIC(18,4)`

The provided sample contains:

- `7,378349`

That means the current schema would round or truncate a real Trade Republic transaction. This must be fixed in the migration plan before any broker-PDF import is implemented.

## 4. Target end-to-end flow

### User flow

1. The user opens a dedicated Trade Republic import page for a portfolio.
2. The user drags 20 to 100 PDFs into the dropzone.
3. The frontend uploads them as `multipart/form-data` to `POST /api/portfolios/{id}/import/broker-pdf`.
4. The API validates the request, stores the files temporarily, creates an import job row, enqueues Hangfire, and returns `202 Accepted` with a domain `jobId`.
5. The frontend starts polling `GET /api/import-jobs/{jobId}` every few seconds.
6. Hangfire processes files one by one:
   - compute file hash
   - extract text from PDF
   - parse Trade Republic fields
   - resolve instrument by ISIN
   - trigger JIT ingestion when needed
   - insert the transaction when the instrument can be mapped to a real ticker
   - update progress after every file
7. If some resolved tickers are still `pending` or `ingesting`, the job moves to `waiting_for_ingestion` instead of holding a Hangfire worker open.
8. The polling endpoint derives remaining ingestion progress from `etf_metadata.status`.
9. Once all items are terminal and all linked tickers are `ready` or `error`, the job becomes terminal.
10. The frontend refreshes the active portfolio automatically and shows the final import summary.

### Expected response shape

The initial upload response should be fast and minimal:

- `jobId`
- `status`
- `totalFiles`
- `message`

The polling response should be richer:

- overall job status
- processed file count vs total
- imported count
- duplicate count
- failed count
- waiting-for-ingestion count
- current file name
- current message
- recent item results
- per-ticker ingestion statuses for the current job

## 5. Core design decisions

### 5.1 Use Hangfire for PDF processing, not Airflow

Reason:

- Hangfire is already configured in `Program.cs`
- PDF parsing is application logic close to the portfolio domain
- Airflow should remain responsible for price-data ingestion, not broker document processing

### 5.2 Use temporary disk storage, not in-memory buffering

Reason:

- 50 to 100 PDFs can create avoidable memory pressure
- ASP.NET request lifetime should stay short
- Hangfire needs access to the files after the request completes

Recommendation:

- write uploads to a dedicated temp root by import-job id
- clean them up after terminal completion
- if container restart resilience becomes important, mount a small persistent volume instead of relying only on `/tmp`

### 5.3 Keep the existing Airflow JIT DAG; add an instrument-resolution bridge before it

Reason:

- the existing `etf_backfill_jit` DAG already works for tickers
- the missing capability is not price ingestion itself; it is ISIN-to-ticker resolution

### 5.4 Do not keep a Hangfire worker busy while waiting for Airflow

Reason:

- `Program.cs` currently starts Hangfire with `WorkerCount = 2`
- a job that sleeps and polls for minutes would starve other background work

Recommendation:

- the worker should parse, resolve, insert, and then exit
- if some tickers are still ingesting, persist that state in the job tables
- `GET /api/import-jobs/{jobId}` should derive live readiness from `etf_metadata`

### 5.5 V1 scope should be intentionally narrow

Recommended V1 support:

- `BUY`
- `SELL`
- savings plan execution mapped to `BUY`

Reason:

- the provided sample dated 2026-03-02 is exactly a savings plan execution and contains enough data to support a deterministic `BUY` mapping

Explicitly out of scope for V1:

- dividends
- tax statements
- cash transfers
- card transactions
- corporate actions

Unsupported documents should be marked as skipped or failed per file, not fail the whole batch.

### 5.6 Preserve broker provenance from day one

Recommended fields to preserve:

- broker name
- broker execution reference if available
- broker secondary reference if available
- document hash
- parsed ISIN
- original broker currency

For Trade Republic specifically, the plan should use:

- `ESECUZIONE ...` as the first-choice idempotency key
- `PIANO DI ACCUMULO ...` as secondary provenance only, not as the unique transaction key

Without this, duplicate detection and later audit/debugging will be weak.

### 5.7 Preserve currency even if analytics remain single-currency for now

The research doc already shows that multi-currency support is incomplete. This feature should not claim full FX-correct portfolio analytics, but it should still store the broker-side transaction currency so imported data is not lossy.

## 6. Phase 0 — Discovery and scope lock

Before implementation starts, lock down the document assumptions with real sample files.

### 6.1 Collect a representative Trade Republic fixture set

Need at least:

- one buy confirmation
- one sell confirmation
- one savings plan execution
- one unsupported document type
- one duplicate sample
- one file that should fail parsing cleanly

### 6.2 Confirm which stable identifiers are present

The provided sample already confirms the following fields are present and extractable:

- ISIN
- execution reference
- recurring plan reference
- instrument name
- units
- price per unit
- gross amount
- currency
- execution date
- value date

The provided sample also confirms these gaps:

- ticker is absent
- fees are not exposed as a separate line item

That suggests the parser contract for this broker should distinguish:

- `transaction_date` = execution date
- `settlement_date` or `value_date` = bookkeeping provenance

If future fixtures expose more variants, validate whether the same fields stay stable across document kinds.

If a stable broker reference exists, use it for idempotency before falling back to file hash.

### 6.3 Confirm locale patterns

Trade Republic PDFs may use:

- decimal comma
- thousands separator
- localized document labels
- localized date formats
- mixed-language metadata and body text

The provided sample confirms:

- metadata title in English: `Savings Plan Execution`
- body text in Italian
- decimal comma formatting for quantity and price
- date format `DD.MM.YYYY` in the document body

The parser should normalize all numeric and date values into invariant server-side types and document-kind detection should use both PDF metadata and body keywords instead of one source only.

### 6.4 Scope lock for V1

Produce a short support matrix:

- supported document types
- unsupported document types
- blocking gaps for full automation

The most important blocking question is whether unknown ISINs can be resolved to a fetchable ticker in a deterministic way.

## 7. Phase 1 — Database foundations

### 7.1 Add a new migration

Create `src/db/09_broker_pdf_import.sql`.

### 7.2 Add import-job status enums

Recommended job-level enum:

- `queued`
- `processing`
- `waiting_for_ingestion`
- `completed`
- `completed_with_errors`
- `failed`

Recommended item-level enum:

- `queued`
- `parsing`
- `parsed`
- `duplicate`
- `unsupported`
- `unresolved_instrument`
- `waiting_for_ingestion`
- `imported`
- `failed`

### 7.3 Add `broker_import_jobs`

Recommended columns:

- `id UUID PRIMARY KEY`
- `portfolio_id UUID NOT NULL`
- `user_id UUID NOT NULL`
- `broker VARCHAR(50) NOT NULL`
- `status broker_import_job_status NOT NULL`
- `hangfire_job_id VARCHAR(50) NULL`
- `total_files INT NOT NULL`
- `processed_files INT NOT NULL DEFAULT 0`
- `imported_files INT NOT NULL DEFAULT 0`
- `duplicate_files INT NOT NULL DEFAULT 0`
- `failed_files INT NOT NULL DEFAULT 0`
- `waiting_for_ingestion_files INT NOT NULL DEFAULT 0`
- `current_file_name TEXT NULL`
- `current_message TEXT NULL`
- `error_summary TEXT NULL`
- `created_at TIMESTAMPTZ`
- `started_at TIMESTAMPTZ`
- `completed_at TIMESTAMPTZ`

### 7.4 Add `broker_import_job_items`

Recommended columns:

- `id UUID PRIMARY KEY`
- `job_id UUID NOT NULL`
- `portfolio_id UUID NOT NULL`
- `original_file_name TEXT NOT NULL`
- `temp_file_path TEXT NOT NULL`
- `file_sha256 CHAR(64) NOT NULL`
- `status broker_import_item_status NOT NULL`
- `broker_reference VARCHAR(100) NULL`
- `broker_secondary_reference VARCHAR(100) NULL`
- `isin VARCHAR(12) NULL`
- `instrument_name TEXT NULL`
- `resolved_ticker VARCHAR(20) NULL`
- `transaction_type VARCHAR(20) NULL`
- `transaction_date DATE NULL`
- `settlement_date DATE NULL`
- `units NUMERIC(18,8) NULL`
- `price_per_unit NUMERIC(18,8) NULL`
- `fees NUMERIC(18,8) NULL`
- `gross_amount NUMERIC(18,8) NULL`
- `currency VARCHAR(3) NULL`
- `created_transaction_id UUID NULL`
- `error_message TEXT NULL`
- `created_at TIMESTAMPTZ`
- `updated_at TIMESTAMPTZ`

### 7.5 Strengthen instrument lookup by ISIN

Recommended schema changes to `etf_metadata`:

- add a partial unique index on `isin` where `isin IS NOT NULL`
- widen `name` beyond the current `VARCHAR(50)` because broker product names often exceed that
- align `etf_metadata.ticker` length with `transactions.ticker` to avoid future mismatch

Reason:

- current import lookup will be ISIN-first
- current metadata length constraints are tight for broker-originated names

### 7.6 Add provenance columns to `transactions`

Recommended nullable columns:

- `source_broker`
- `source_reference`
- `source_secondary_reference`
- `source_document_hash`
- `source_isin`
- `trade_currency`

Recommended unique indexes:

- unique by `(portfolio_id, source_broker, source_reference)` when reference is present
- unique by `(portfolio_id, source_broker, source_document_hash)` as fallback

This prevents duplicate inserts if the same PDFs are uploaded again.

For the provided Trade Republic savings-plan sample:

- `source_reference` should map to the execution id
- `source_secondary_reference` can preserve the savings-plan id

### 7.7 Increase numeric precision on transactions

Recommended schema changes to `transactions`:

- change `units` from `NUMERIC(18,4)` to at least `NUMERIC(18,8)`
- align `price_per_unit` and `fees` to `NUMERIC(18,8)` for consistency with imported broker data

Reason:

- the provided sample quantity `7,378349` does not fit safely in the current live schema

### 7.8 Tenancy and authorization

New job tables should carry `user_id` and always be queried with guest ownership filters.

Do not repeat the current pattern used in `CsvImportController` and `PortfoliosController` where raw `SELECT EXISTS` checks ignore guest ownership.

## 8. Phase 2 — API endpoints and job orchestration

### 8.1 New API endpoints

Recommended routes:

- `POST /api/portfolios/{id}/import/broker-pdf`
- `GET /api/import-jobs/{jobId}`

Optional future route:

- `POST /api/import-jobs/{jobId}/retry-failed`

### 8.2 Upload endpoint behavior

`POST /api/portfolios/{id}/import/broker-pdf` should:

- resolve `userId` from `HttpContext.GetGuestId()`
- verify portfolio ownership, not just portfolio existence
- validate count, size, extension, and MIME type
- reject empty uploads early
- create a domain job id up front
- save each file to a temp folder such as `broker-imports/{jobId}/`
- insert one job row and one item row per file
- enqueue a Hangfire job with the domain job id
- return `202 Accepted`

### 8.3 Use a domain job id, not the Hangfire job id, as the public contract

Reason:

- Hangfire job ids are infrastructure details
- the API should own its import-job lifecycle
- the polling endpoint needs domain-aware aggregation, not raw Hangfire state

Store Hangfire’s job id internally for troubleshooting only.

### 8.4 New service and repository split

Recommended additions:

- `IBrokerPdfImportService` in `EtfInsight.Core`
- `IBrokerImportRepository` in `EtfInsight.Core`
- `BrokerPdfImportService` in `EtfInsight.Infrastructure`
- `DapperBrokerImportRepository` in `EtfInsight.Infrastructure`
- thin controller in `EtfInsight.Api`

This keeps the implementation aligned with the existing project split instead of adding more controller-heavy logic.

### 8.5 Hangfire execution model

Recommended job method:

- `ProcessTradeRepublicImportAsync(Guid importJobId, Guid userId, CancellationToken ct)`

Recommended behavior:

- mark job `processing`
- process items sequentially for predictable memory and CPU usage
- update job progress after every file
- exit quickly once all parsing and inserts are done
- if any linked tickers are not yet `ready`, mark job `waiting_for_ingestion`

### 8.6 Polling endpoint behavior

`GET /api/import-jobs/{jobId}` should:

- verify ownership by `jobId + userId`
- return current persisted progress
- for any distinct `resolved_ticker` linked to this job, join `etf_metadata.status`
- compute whether the job can now be considered terminal
- expose recent item-level results for the UI

This endpoint becomes the single source of truth for the browser progress bar.

## 9. Phase 3 — PDF extraction and Trade Republic parsing

### 9.1 Package

Add `PdfPig` (NuGet id: `UglyToad.PdfPig`) to `EtfInsight.Infrastructure.csproj`.

No other third-party PDF parser needed for V1. PdfPig is pure managed code with no native dependencies, which keeps the Docker image simple.

### 9.2 File and namespace structure

New files:

```
src/EtfInsight.Core/Interfaces/
  IPdfTextExtractor.cs
  ITradeRepublicParser.cs

src/EtfInsight.Core/DTOs/
  ParsedTransactionResult.cs        ← parsed output model
  TradeRepublicParseResult.cs       ← discriminated union

src/EtfInsight.Infrastructure/Services/BrokerPdf/
  PdfPigTextExtractor.cs
  TradeRepublicTextNormalizer.cs
  TradeRepublicDocumentKindDetector.cs
  TradeRepublicParser.cs

tests/EtfInsight.Tests/BrokerPdf/
  Fixtures/
    savings_plan_execution_REDACTED.pdf
    buy_confirmation_REDACTED.pdf
    sell_confirmation_REDACTED.pdf
    unsupported_dividend_REDACTED.pdf
    empty.pdf
  TradeRepublicTextNormalizerTests.cs
  TradeRepublicDocumentKindDetectorTests.cs
  TradeRepublicParserTests.cs
```

Namespace convention:

- implementation types → `EtfInsight.Infrastructure.Services.BrokerPdf`
- interface and model types → `EtfInsight.Core.Interfaces` / `EtfInsight.Core.DTOs`

### 9.3 Interface contracts

#### `IPdfTextExtractor`

Lives in `EtfInsight.Core/Interfaces/IPdfTextExtractor.cs`.

Single method:

```csharp
Task<PdfExtractionResult> ExtractAsync(string filePath, CancellationToken ct = default);
```

`PdfExtractionResult` is a record defined in the same file:

| Property  | Type      | Notes                                          |
| --------- | --------- | ---------------------------------------------- |
| `Title`   | `string?` | Value of the PDF `/Title` metadata field       |
| `RawText` | `string`  | Full concatenated text from all pages in order |

#### `ITradeRepublicParser`

Lives in `EtfInsight.Core/Interfaces/ITradeRepublicParser.cs`.

Single method:

```csharp
TradeRepublicParseResult Parse(PdfExtractionResult extraction);
```

No async: parsing is pure CPU work on already-extracted text. The interface is thin; the implementation (`TradeRepublicParser`) owns the normalizer, detector, and regex logic internally.

### 9.4 `ParsedTransactionResult` model

Lives in `src/EtfInsight.Core/DTOs/ParsedTransactionResult.cs`.

| Property                   | C# type     | Source field in document   | Notes                                 |
| -------------------------- | ----------- | -------------------------- | ------------------------------------- |
| `BrokerReference`          | `string?`   | `ESECUZIONE …`             | Primary idempotency key               |
| `BrokerSecondaryReference` | `string?`   | `PIANO DI ACCUMULO …`      | Provenance only                       |
| `InstrumentName`           | `string?`   | Line above `ISIN:`         | May be null if extraction fails       |
| `Isin`                     | `string`    | `ISIN:` label              | Required; 12-char validated           |
| `TransactionType`          | `string`    | Derived from document kind | `"BUY"` or `"SELL"`                   |
| `TransactionDate`          | `DateOnly`  | Execution date field       | Not the value/settlement date         |
| `SettlementDate`           | `DateOnly?` | `DATA VALUTA`              | Provenance only                       |
| `Units`                    | `decimal`   | `QUANTITÀ`                 | 6 decimal places from sample          |
| `PricePerUnit`             | `decimal`   | `PREZZO MEDIO`             | 4 decimal places from sample          |
| `Fees`                     | `decimal?`  | Optional fee line          | null if absent                        |
| `GrossAmount`              | `decimal`   | `TOTALE`                   | Cross-check; not persisted separately |
| `Currency`                 | `string`    | Code adjacent to amounts   | 3-char ISO                            |

### 9.5 `TradeRepublicParseResult` discriminated union

Lives in `src/EtfInsight.Core/DTOs/TradeRepublicParseResult.cs`.

Three concrete subtypes:

| Case          | Condition                                    | Carrier fields                        |
| ------------- | -------------------------------------------- | ------------------------------------- |
| `Success`     | All required fields extracted                | `ParsedTransactionResult Transaction` |
| `Unsupported` | Valid TR PDF, document kind outside V1 scope | `string Reason`                       |
| `Failure`     | Parse error or missing required field        | `string Reason`, `string Stage`       |

`Stage` examples: `"extraction"`, `"detection"`, `"isin"`, `"units"`, `"transaction_date"`.

This union is the only return type of `ITradeRepublicParser.Parse`. The caller switches on the subtype instead of catching exceptions for expected outcomes.

### 9.6 Document kind

```csharp
enum TradeRepublicDocumentKind
{
    Unknown,
    BuyConfirmation,
    SellConfirmation,
    SavingsPlanExecution,
    Dividend,       // unsupported in V1
    Tax,            // unsupported in V1
    CashMovement    // unsupported in V1
}
```

V1 only processes `BuyConfirmation`, `SellConfirmation`, and `SavingsPlanExecution`. All others produce an `Unsupported` result.

### 9.7 Detection strategy

`TradeRepublicDocumentKindDetector.Detect(string? pdfTitle, string normalizedBody)` applies checks in order:

**Step 1 — PDF title (case-insensitive):**

| Title contains             | Body also contains | Result                 |
| -------------------------- | ------------------ | ---------------------- |
| `"savings plan execution"` | —                  | `SavingsPlanExecution` |
| `"order confirmation"`     | `"ACQUISTO"`       | `BuyConfirmation`      |
| `"order confirmation"`     | `"VENDITA"`        | `SellConfirmation`     |
| `"dividend"`               | —                  | `Dividend`             |

**Step 2 — Body keyword fallback (if title is null or unrecognized):**

| Body contains         | Result                 |
| --------------------- | ---------------------- |
| `"PIANO DI ACCUMULO"` | `SavingsPlanExecution` |
| `"ACQUISTO"`          | `BuyConfirmation`      |
| `"VENDITA"`           | `SellConfirmation`     |
| `"DIVIDENDO"`         | `Dividend`             |

**Step 3:** If still unresolved → `Unknown`.

Both sources are checked because the confirmed sample has an English PDF title and an Italian body.

### 9.8 Text normalization rules

`TradeRepublicTextNormalizer.Normalize(string rawText)` applies in order:

1. Replace `\r\n` and `\r` with `\n`
2. Replace zero-width and non-breaking spaces (`\u200B`, `\u00A0`, `\uFEFF`) with regular space
3. Collapse runs of whitespace-only characters within a line to a single space
4. Trim leading and trailing whitespace per line
5. Collapse more than two consecutive blank lines to a single blank line

Do **not** convert decimal commas globally and do **not** convert date strings globally at this stage. These conversions happen field-by-field inside the parser to avoid misidentifying thousands separators.

### 9.9 Regex anchors per document kind

All patterns use `Regex.Match` on the full normalized text. Options: `RegexOptions.IgnoreCase | RegexOptions.Multiline`.

#### Common to `BuyConfirmation`, `SellConfirmation`, `SavingsPlanExecution`:

> Patterns verified against `test.pdf` (savings plan execution, Italian locale, 2026-03-02).

| Field            | Pattern                                  | Status                                                                           |
| ---------------- | ---------------------------------------- | -------------------------------------------------------------------------------- |
| ISIN             | `ISIN:\s*([A-Z]{2}[A-Z0-9]{9}\d)`        | ✓ verified                                                                       |
| Execution ref    | `ESECUZIONE\s+([A-Za-z0-9\-]+)`          | ✓ verified                                                                       |
| Plan ref         | `PIANO DI ACCUMULO\s+([A-Za-z0-9\-]+)`   | ✓ verified; savings plan docs only                                               |
| Gross amount     | `TOTALE\s+([\d,]+)\s+([A-Z]{3})`         | ✓ verified                                                                       |
| Transaction date | `\bDATA\s+(\d{2}\.\d{2}\.\d{4})`         | ✓ verified; label is `DATA`, **not** `DATA DI ESECUZIONE` or `DATA OPERAZIONE`   |
| Settlement date  | `DATA VALUTA[\s\S]*?(\d{4}-\d{2}-\d{2})` | ✓ verified; format is ISO `YYYY-MM-DD` across a line break, **not** `DD.MM.YYYY` |

Notes:

- `\bDATA\s+(\d{2}\.\d{2}\.\d{4})` does **not** accidentally match `DATA VALUTA` because `VALUTA` is not `\d{2}\.\d{2}\.\d{4}`.
- The settlement date regex uses `[\s\S]*?` (non-greedy, matches the newline between the `DATA VALUTA IMPORTO` header and the IBAN data line).

#### Instrument data row — units, price per unit, and instrument name

`QUANTITÀ` and `PREZZO MEDIO` are **column headers** in the document, not field labels. Their values appear on the instrument data row that immediately follows the header line `POSIZIONE QUANTITÀ PREZZO MEDIO IMPORTO`. Extract all three fields together with one right-anchored pattern (options: `IgnoreCase | Multiline`):

```
POSIZIONE QUANTIT[AÀ] PREZZO MEDIO IMPORTO\n(.+?)\s+([\d]+,[\d]+)\s+([\d]+,[\d]+)\s+EUR\s+([\d]+,[\d]+)\s+EUR
```

Capture groups:

| Group | Field                                  | Verified example            |
| ----- | -------------------------------------- | --------------------------- |
| 1     | Instrument name                        | `Core MSCI World USD (Acc)` |
| 2     | Units                                  | `7,378349`                  |
| 3     | Price per unit                         | `113,8466`                  |
| 4     | Gross amount (cross-check vs `TOTALE`) | `840,00`                    |

Currency is taken from the fixed `EUR` literals on the same data line; fall back to the `TOTALE` line if absent.

This replaces the backwards-scan instrument-name approach. The table-row pattern returns the name cleanly as group 1 because all numeric fields are right-anchored at the end of the line.

### 9.10 Field parsing rules

**Decimal fields** (units, price, gross amount, fees):

```
step 1: remove all '.' characters (Trade Republic uses '.' as a thousands separator in some locales)
step 2: replace ',' with '.'
step 3: decimal.Parse(result, CultureInfo.InvariantCulture)
```

**Date fields:**

- Transaction date: `DateOnly.ParseExact(captured, "dd.MM.yyyy", CultureInfo.InvariantCulture)` — confirmed format from `DATA 02.03.2026`
- Settlement date: `DateOnly.ParseExact(captured, "yyyy-MM-dd", CultureInfo.InvariantCulture)` — confirmed ISO format from `2026-03-04` on the CONTO DI TRANSITO line

Do **not** use a single format for both fields; they differ in this document.

**ISIN validation** (after regex capture):

- exact length 12
- characters 0–1: `[A-Z]{2}`
- characters 2–11: `[A-Z0-9]{10}`
- Luhn mod-10 checksum is optional for V1 but recommended as an assertion in the parser unit tests

### 9.11 Duplicate detection — new repository method

Add to `IBrokerImportRepository`:

```csharp
Task<bool> IsDocumentAlreadyImportedAsync(
    Guid portfolioId,
    string fileSha256,
    string? brokerReference,
    CancellationToken ct = default);
```

`DapperBrokerImportRepository` implements this with a query against `transactions` (not `broker_import_job_items`), so a document that was imported in a previous job is still detected:

```sql
SELECT EXISTS (
    SELECT 1 FROM transactions
    WHERE portfolio_id = @PortfolioId
    AND (
        source_document_hash = @FileSha256
        OR (
            source_broker = 'trade_republic'
            AND source_reference = @BrokerReference
            AND @BrokerReference IS NOT NULL
        )
    )
)
```

Hash is checked first. Broker reference serves as a secondary match since the same execution id can only exist once per portfolio.

This call occurs after a successful parse, before any transaction insert, while the item is still at `parsed` status.

### 9.12 Integration into `ProcessTradeRepublicImportAsync`

The `TODO Phase 3` stub in `BrokerPdfImportService.ProcessTradeRepublicImportAsync` expands into the following per-item sequence. Phase 4 (instrument resolution and insert) begins at step 10 and is **not** part of Phase 3:

```
 1. UpdateItemStatus(item.Id, "parsing")
 2. result ← extractor.ExtractAsync(item.TempFilePath, ct)
 3. normalized ← normalizer.Normalize(result.RawText)
 4. kind ← detector.Detect(result.Title, normalized)
 5. if kind ∉ {BuyConfirmation, SellConfirmation, SavingsPlanExecution}:
       → UpdateItem(status="unsupported", errorMessage="Document kind not supported in V1: {kind}")
       → continue to next item
 6. parseResult ← parser.Parse(result)
 7. if parseResult is Failure:
       → UpdateItem(status="failed", errorMessage="{Stage}: {Reason}")
       → continue
 8. if parseResult is Unsupported:
       → UpdateItem(status="unsupported", errorMessage=parseResult.Reason)
       → continue
 9. isDuplicate ← IsDocumentAlreadyImportedAsync(item.PortfolioId, item.FileSha256, parsed.BrokerReference, ct)
10. if isDuplicate:
       → UpdateItem(status="duplicate")
       → continue
11. UpdateItem with all parsed fields, status="parsed"
    (Phase 4 picks up from here)
```

The `TODO Phase 4` comment replacing the old `TODO Phase 3` comment makes the handoff boundary explicit.

### 9.13 Unsupported and failed item handling

- `unsupported` items: valid TR PDF, kind not in V1 scope. Counted separately in job counters.
- `failed` items: extraction error, parse error, or missing required field.
- Neither stops the batch; both are terminal per-item states.
- `failed_files` counter in `broker_import_jobs` counts only `failed` items. `unsupported` items will not be counted in `failed_files`; they will be surfaced in a future `unsupported_files` counter if needed (out of scope for Phase 3).

### 9.14 Diagnostics stored per failed item

`error_message` is capped at 500 chars. Include:

- stage name (`"isin"`, `"units"`, `"transaction_date"`, etc.)
- reason (`"ISIN not found in text"`, `"Units decimal parse failed: '7,378,349'"`)

Do not store the full raw PDF text or the full normalized text in the DB row.

### 9.15 Test project adjustment

`tests/EtfInsight.Tests/EtfInsight.Tests.csproj` currently references only `EtfInsight.Core`.

Add a `<ProjectReference>` to `EtfInsight.Infrastructure` to allow testing `BrokerPdf` classes directly.

PDF fixtures in `tests/EtfInsight.Tests/BrokerPdf/Fixtures/` should be declared as:

```xml
<Content Include="BrokerPdf/Fixtures/**" CopyToOutputDirectory="PreserveNewest" />
```

so tests run correctly after `dotnet test` without manual file copying.

## 10. Phase 4 — Instrument resolution and JIT ingestion reuse

### 10.1 Resolution order

Recommended lookup order:

1. find `etf_metadata` by `isin`
2. if found, use the existing `ticker`
3. if not found, call a new `IInstrumentResolutionService`
4. if resolution succeeds, upsert `etf_metadata` with `ticker + isin + name`
5. call the existing JIT ingestion path
6. if resolution fails, mark the item `unresolved_instrument`

### 10.2 New resolver abstraction

Add:

- `IInstrumentResolutionService`

Purpose:

- isolate the unavoidable `ISIN -> ticker` bridge
- keep broker parsing separate from market-data provider logic

This service can evolve independently from the PDF parser.

### 10.3 Extend ingestion service contract

The current `IIngestionService.EnsureTickerReadyAsync(string ticker)` is too narrow for broker import because metadata hints already exist in the PDF.

Recommended change:

- add an overload or a new method that accepts `ticker`, `isin`, and `name`

Reason:

- placeholder metadata inserted during JIT should keep the ISIN and product name when known
- broker import should not discard richer metadata and replace it with `name = ticker`

### 10.4 Do not invent fake tickers

If an unknown ISIN cannot be mapped to a real ticker:

- do not create a synthetic ticker just to satisfy the foreign key
- do not insert a transaction that analytics cannot value correctly

Instead:

- keep the item failed with a clear unresolved-instrument reason

### 10.5 Transaction insertion timing

Recommended timing:

- insert the transaction only after a real ticker is known
- do not wait for prices to finish loading if the ticker is already resolved

For Trade Republic savings-plan documents, use the execution date as the inserted transaction date. Preserve value date separately as provenance if needed, but do not use it as the holding acquisition date for analytics.

This stays consistent with the current JIT pattern:

- the transaction can exist before prices are ready
- analytics become complete once `etf_metadata.status` reaches `ready`

### 10.6 Job completion strategy

If every file is parsed and every possible transaction has been inserted, but some linked tickers are still `pending` or `ingesting`:

- job status becomes `waiting_for_ingestion`

The polling endpoint should then expose:

- pending tickers
- their current `etf_metadata.status`
- remaining count

Once all linked tickers are terminal:

- `completed` if everything succeeded
- `completed_with_errors` if some files failed, were unsupported, or were duplicates

## 11. Phase 5 — Frontend UX

### 11.1 New route and view

Recommended additions:

- `frontend/src/views/BrokerPdfImportView.vue`
- route `/portfolios/:id/import/broker-pdf`

Keep CSV import and broker-PDF import as separate views for now. They solve different user problems and have different progress states.

### 11.2 New component

Create `frontend/src/components/portfolios/BrokerImportDropzone.vue`.

Responsibilities:

- multi-file drag and drop
- file picker with `multiple`
- accept only `.pdf`
- show selected file count and names
- show upload button and disabled state

Unlike CSV import, no client-side content preview is needed.

### 11.3 New frontend API layer

Recommended additions:

- `frontend/src/api/importJobs.ts` for polling
- either extend `frontend/src/api/portfolios.ts` with `importBrokerPdf(...)` or keep it in the new file

### 11.4 New polling composable

Create `frontend/src/composables/useImportJobPolling.ts`.

Responsibilities:

- poll `GET /api/import-jobs/{jobId}` every 2 to 3 seconds
- stop on terminal state
- expose:
  - overall status
  - percent complete
  - current message
  - recent file-level outcomes
  - pending tickers

Do not reuse `useIngestionPolling.ts`; it is ticker-only and not used elsewhere today.

### 11.5 UI states

Recommended UI states:

- idle
- uploading
- queued
- processing
- waiting for market data
- completed
- completed with warnings
- failed

### 11.6 Progress messaging

Examples of the kind of messages the backend should provide:

- `Processing PDF 1 of 20`
- `Parsed BUY for VWCE`
- `Resolved ISIN to VUSA.MI`
- `Triggered price ingestion for 3 new instruments`
- `Waiting for Airflow to finish 2 instruments`
- `18 transactions imported, 2 files skipped`

### 11.7 Integration with the existing portfolio store

On terminal success:

- refresh portfolios via the existing `usePortfoliosStore`
- reload the active portfolio dashboard and summary

Optional enhancement:

- also register pending tickers in the existing `useIngestionStore` so the sidebar spinner stays coherent if the user navigates away during import

## 12. Phase 6 — Limits, cleanup, and observability

### 12.1 Upload limits

Update `frontend/nginx.conf` with an explicit `client_max_body_size` appropriate for the expected batch size.

Also configure ASP.NET multipart limits if needed.

### 12.2 Temp-file lifecycle

Recommended temp directory layout:

- one folder per job id
- sanitized original filenames

Cleanup strategy:

- delete files after terminal completion
- add a recurring cleanup job for abandoned temp folders and stale DB rows

### 12.3 Logging

Every important log line should include:

- import job id
- item id when relevant
- portfolio id
- resolved ticker or ISIN when relevant

### 12.4 Hangfire queue separation

Recommended if throughput becomes an issue:

- add a dedicated queue for broker imports

Reason:

- PDF parsing should not compete directly with data-quality jobs forever

### 12.5 Partial failure handling

A batch should never be all-or-nothing unless the upload itself is invalid.

Per-file failure must be isolated for:

- duplicate files
- unsupported document type
- parse failure
- unresolved instrument
- transaction insert failure
- JIT trigger failure

## 13. Testing plan

### 13.1 Parser unit tests

Add sanitized Trade Republic PDF fixtures and cover:

- buy document
- sell document
- savings plan document
- unsupported document
- malformed document
- locale-specific decimals and dates
- execution date vs value date
- missing ticker
- missing explicit fee line
- mixed-language metadata/body detection
- 6-decimal fractional quantity handling

### 13.2 Service tests

Cover:

- duplicate detection by hash or broker reference
- execution id preferred over recurring plan id for idempotency
- unresolved ISIN
- existing ISIN already in `etf_metadata`
- existing ticker already `ready`
- new ticker triggering JIT
- mixed batch with success and failure

### 13.3 API tests

Cover:

- upload returns `202` quickly
- job row is created
- ownership checks on upload and status polling
- invalid file extension or empty batch

### 13.4 Integration tests

Cover the end-to-end application path with a fake or stubbed resolver and ingestion service:

- upload multiple PDFs
- background processing updates job state
- transactions are inserted
- polling endpoint shows progress

### 13.5 E2E smoke test

Add an opt-in smoke test similar in spirit to `tests/e2e/test_jit_ingestion_smoke.py`:

- create portfolio
- upload a small PDF batch
- poll job status until terminal
- verify portfolio shows imported transactions

### 13.6 Test project structure note

Today `tests/EtfInsight.Tests/EtfInsight.Tests.csproj` references only `EtfInsight.Core`.

To test parser and infrastructure services, either:

- add `EtfInsight.Infrastructure` as a test project reference
- or create a dedicated infrastructure test project

## 14. Implementation sequence

1. Lock the document support matrix with real Trade Republic fixtures.
2. Add DB migration for jobs, items, provenance, and ISIN constraints.
3. Add backend repository and job DTOs.
4. Add upload endpoint and polling endpoint.
5. Add Hangfire job orchestration without parser logic first.
6. Add PdfPig extraction and parser with unit tests.
7. Add ISIN resolution bridge and metadata upsert improvements.
8. Reuse the existing Airflow JIT flow for resolved tickers.
9. Add the Vue upload page, polling composable, and progress UI.
10. Add cleanup, limits, logging, and final integration tests.

## 15. Todo List

Legend: `[ ]` not started · `[~]` in progress · `[x]` done

### Phase 0 — Discovery

- [x] Collect and sanitize a representative Trade Republic PDF fixture set
- [x] Confirm which stable identifiers exist in the PDF text
- [x] Confirm locale and formatting variants
- [x] Freeze the V1 support matrix

### Phase 1 — Database

- [x] Create `09_broker_pdf_import.sql`
- [x] Add `broker_import_jobs`
- [x] Add `broker_import_job_items`
- [x] Add partial unique index on `etf_metadata.isin`
- [x] Widen `etf_metadata` name and ticker columns if needed
- [x] Add transaction provenance and trade-currency columns
- [x] Increase `transactions` numeric precision for broker fractional units
- [x] Add duplicate-prevention indexes

### Phase 2 — Backend API and orchestration

- [x] Add broker import DTOs and interfaces in `EtfInsight.Core`
- [x] Add `DapperBrokerImportRepository`
- [x] Add `BrokerPdfImportService`
- [x] Add `POST /api/portfolios/{id}/import/broker-pdf`
- [x] Add `GET /api/import-jobs/{jobId}`
- [x] Enqueue import processing through Hangfire
- [x] Enforce guest ownership checks on both endpoints

### Phase 3 — PDF parsing

- [x] Add `UglyToad.PdfPig` NuGet to `EtfInsight.Infrastructure.csproj`
- [x] Add `IPdfTextExtractor` interface and `PdfExtractionResult` record to `EtfInsight.Core`
- [x] Implement `PdfPigTextExtractor` in `EtfInsight.Infrastructure/Services/BrokerPdf/`
- [x] Add `ITradeRepublicParser` interface to `EtfInsight.Core`
- [x] Add `ParsedTransactionResult` DTO to `EtfInsight.Core/DTOs/`
- [x] Add `TradeRepublicParseResult` discriminated union (`Success`, `Unsupported`, `Failure`) to `EtfInsight.Core/DTOs/`
- [x] Implement `TradeRepublicTextNormalizer` (whitespace, CRLF, zero-width chars)
- [x] Implement `TradeRepublicDocumentKindDetector` (PDF title + Italian body keywords)
- [x] Implement `TradeRepublicParser` with regex rule sets for `BuyConfirmation`, `SellConfirmation`, `SavingsPlanExecution`
- [x] Implement instrument name extraction (backwards-scan from `ISIN:` line)
- [x] Implement per-field decimal parsing (strip thousands `.`, convert `,` to `.`)
- [x] Implement per-field date parsing (`dd.MM.yyyy` → `DateOnly`)
- [x] Add `IsDocumentAlreadyImportedAsync` to `IBrokerImportRepository` and `DapperBrokerImportRepository`
- [x] Replace `TODO Phase 3` stub in `ProcessTradeRepublicImportAsync` with full parse-and-duplicate-check loop (steps 1–11 per plan)
- [x] Add `<ProjectReference>` to `EtfInsight.Infrastructure` in the test project
- [x] Add PDF fixtures to `tests/EtfInsight.Tests/BrokerPdf/Fixtures/` with `CopyToOutputDirectory`
- [x] Add unit tests: normalizer, detector, parser (all document kinds + failure modes)

### Phase 4 — Instrument resolution and JIT

- [x] Add `IInstrumentResolutionService`
- [x] Implement `ISIN -> ticker` resolution strategy
- [x] Extend ingestion contract to preserve ISIN and name metadata
- [x] Reuse `AirflowIngestionService` for resolved tickers
- [x] Derive import-job readiness from `etf_metadata.status`

### Phase 5 — Frontend

- [ ] Add `BrokerImportDropzone.vue`
- [ ] Add `BrokerPdfImportView.vue`
- [ ] Add route `/portfolios/:id/import/broker-pdf`
- [ ] Add upload API call
- [ ] Add job-polling composable
- [ ] Add progress bar and recent-results UI
- [ ] Refresh portfolio data on terminal success

### Phase 6 — Hardening

- [ ] Raise Nginx upload limits
- [ ] Configure ASP.NET multipart limits if needed
- [ ] Add temp-file cleanup
- [ ] Add structured logging around job id and item id
- [ ] Consider dedicated Hangfire queue for imports
- [ ] Add parser, service, API, and integration tests
