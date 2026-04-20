# Plan: Broker Import Archive UI

Last updated: 2026-04-20

## Objective

Expose a read-only archive of all broker PDF import sessions for a portfolio. A user opens the archive from the portfolio view, sees every past import job with its summary counters, and can drill into any job to inspect the per-file item rows stored in `broker_import_job_items`. No new background processing is involved — this is a pure read path on top of data that already exists.

---

## Approach

Extend the existing `BrokerPdfImportController`, `IBrokerImportRepository`, `DapperBrokerImportRepository`, and `IBrokerPdfImportService` with two new read methods each. Add three new C# DTOs in `EtfInsight.Core`. On the frontend, add two new views, two new routes, three new TypeScript types, two new API functions, and a navigation button in `PortfoliosView.vue`. No new service class, no new controller, no database migration.

**Alternatives considered:**

- A single combined endpoint returning jobs + all items for all jobs: rejected — item lists can be 100 rows per job × many sessions. On-demand per-job item loading is the right model.
- Reusing `ImportJobItemResult` for the detail view: rejected — that type is the live-polling shape (5 fields only). The detail view needs all 16 item columns. A new DTO avoids changing the live-polling response surface.

---

## Out of Scope

- Editing, retrying, or deleting import jobs.
- Pagination (acceptable for V1; volume per portfolio is expected to remain small).
- Any changes to background processing logic.
- Multi-portfolio archive views.

---

## Files to Modify

| File | Change |
|---|---|
| `src/EtfInsight.Core/Interfaces/IBrokerImportRepository.cs` | Add `GetJobsByPortfolioAsync` method signature |
| `src/EtfInsight.Core/Interfaces/IBrokerPdfImportService.cs` | Add `GetJobsByPortfolioAsync` and `GetJobDetailAsync` method signatures |
| `src/EtfInsight.Infrastructure/Repositories/DapperBrokerImportRepository.cs` | Implement `GetJobsByPortfolioAsync` |
| `src/EtfInsight.Infrastructure/Services/BrokerPdfImportService.cs` | Implement `GetJobsByPortfolioAsync` and `GetJobDetailAsync` |
| `src/EtfInsight.Api/Controllers/BrokerPdfImportController.cs` | Add `GET /api/portfolios/{portfolioId}/import-jobs` and `GET /api/import-jobs/{jobId}/items` |
| `frontend/src/api/importJobs.ts` | Add `getByPortfolio` and `getDetail` functions |
| `frontend/src/types/index.ts` | Add `BrokerImportJobSummary`, `BrokerImportItemDetail`, `BrokerImportJobDetail` |
| `frontend/src/router/index.ts` | Add two new routes |
| `frontend/src/views/PortfoliosView.vue` | Add "Import Archive" `RouterLink` button in the actions row |

## Files to Create

| File | Responsibility |
|---|---|
| `src/EtfInsight.Core/DTOs/ImportJobSummaryResponse.cs` | Lean DTO for the job-list endpoint (13 fields) |
| `src/EtfInsight.Core/DTOs/ImportJobItemDetail.cs` | Rich DTO for item rows in the detail endpoint (16 fields, no `temp_file_path`) |
| `src/EtfInsight.Core/DTOs/ImportJobDetailResponse.cs` | Job header + `IReadOnlyList<ImportJobItemDetail>` for the detail endpoint |
| `frontend/src/views/BrokerImportArchiveView.vue` | Lists all import jobs for a portfolio; rows navigate to the detail view on click |
| `frontend/src/views/BrokerImportJobDetailView.vue` | Shows job header cards and the full item table for a single import job |

---

## Implementation

### 1. Repository — `GetJobsByPortfolioAsync`

`GetItemsAsync(Guid jobId, ...)` already exists on `IBrokerImportRepository` and returns all items for one job. It is reused as-is by the detail service method — no modification needed.

Add one new method to `IBrokerImportRepository`:

```csharp
Task<IReadOnlyList<BrokerImportJob>> GetJobsByPortfolioAsync(
    Guid portfolioId, Guid userId, CancellationToken ct = default);
```

Implement in `DapperBrokerImportRepository`. The column alias list is identical to the existing `GetJobAsync` query:

```csharp
public async Task<IReadOnlyList<BrokerImportJob>> GetJobsByPortfolioAsync(
    Guid portfolioId, Guid userId, CancellationToken ct = default)
{
    var rows = await db.QueryAsync<BrokerImportJob>(
        """
        SELECT id, portfolio_id AS PortfolioId, user_id AS UserId, broker, status,
               hangfire_job_id AS HangfireJobId, total_files AS TotalFiles,
               processed_files AS ProcessedFiles, imported_files AS ImportedFiles,
               duplicate_files AS DuplicateFiles, failed_files AS FailedFiles,
               waiting_for_ingestion_files AS WaitingForIngestionFiles,
               current_file_name AS CurrentFileName, current_message AS CurrentMessage,
               error_summary AS ErrorSummary, created_at AS CreatedAt,
               started_at AS StartedAt, completed_at AS CompletedAt
        FROM broker_import_jobs
        WHERE portfolio_id = @PortfolioId
          AND user_id = @UserId
        ORDER BY created_at DESC
        """,
        new { PortfolioId = portfolioId, UserId = userId });

    return rows.ToList();
}
```

### 2. New C# DTOs

**`ImportJobSummaryResponse.cs`** — job list shape:

```csharp
namespace EtfInsight.Core.DTOs;

public record ImportJobSummaryResponse(
    Guid JobId,
    string Broker,
    string Status,
    int TotalFiles,
    int ProcessedFiles,
    int ImportedFiles,
    int DuplicateFiles,
    int FailedFiles,
    int WaitingForIngestionFiles,
    string? ErrorSummary,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt
);
```

**`ImportJobItemDetail.cs`** — rich item row for the detail view:

```csharp
namespace EtfInsight.Core.DTOs;

public record ImportJobItemDetail(
    string FileName,
    string Status,
    string? Isin,
    string? InstrumentName,
    string? ResolvedTicker,
    string? TransactionType,
    DateOnly? TransactionDate,
    DateOnly? SettlementDate,
    decimal? Units,
    decimal? PricePerUnit,
    decimal? Fees,
    decimal? GrossAmount,
    string? Currency,
    string? BrokerReference,
    string? BrokerSecondaryReference,
    string? ErrorMessage
);
```

**`ImportJobDetailResponse.cs`** — job header + full item list:

```csharp
namespace EtfInsight.Core.DTOs;

public record ImportJobDetailResponse(
    Guid JobId,
    string Broker,
    string Status,
    int TotalFiles,
    int ImportedFiles,
    int DuplicateFiles,
    int FailedFiles,
    string? ErrorSummary,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<ImportJobItemDetail> Items
);
```

### 3. Service — two new methods

Add to `IBrokerPdfImportService`:

```csharp
Task<IReadOnlyList<ImportJobSummaryResponse>?> GetJobsByPortfolioAsync(
    Guid portfolioId, Guid userId, CancellationToken ct = default);

Task<ImportJobDetailResponse?> GetJobDetailAsync(
    Guid jobId, Guid userId, CancellationToken ct = default);
```

Implement in `BrokerPdfImportService`. Both methods use the already-injected `portfolioRepository` and `brokerImportRepository` constructor parameters:

```csharp
public async Task<IReadOnlyList<ImportJobSummaryResponse>?> GetJobsByPortfolioAsync(
    Guid portfolioId, Guid userId, CancellationToken ct = default)
{
    var portfolio = await portfolioRepository.GetByIdAndUserAsync(portfolioId, userId, ct);
    if (portfolio is null)
        return null;

    var jobs = await brokerImportRepository.GetJobsByPortfolioAsync(portfolioId, userId, ct);

    return jobs
        .Select(j => new ImportJobSummaryResponse(
            j.Id, j.Broker, j.Status, j.TotalFiles, j.ProcessedFiles,
            j.ImportedFiles, j.DuplicateFiles, j.FailedFiles,
            j.WaitingForIngestionFiles, j.ErrorSummary,
            j.CreatedAt, j.StartedAt, j.CompletedAt))
        .ToList();
}

public async Task<ImportJobDetailResponse?> GetJobDetailAsync(
    Guid jobId, Guid userId, CancellationToken ct = default)
{
    var job = await brokerImportRepository.GetJobAsync(jobId, userId, ct);
    if (job is null)
        return null;

    var items = await brokerImportRepository.GetItemsAsync(jobId, ct);

    var itemDetails = items
        .Select(i => new ImportJobItemDetail(
            i.OriginalFileName, i.Status, i.Isin, i.InstrumentName,
            i.ResolvedTicker, i.TransactionType, i.TransactionDate,
            i.SettlementDate, i.Units, i.PricePerUnit, i.Fees,
            i.GrossAmount, i.Currency, i.BrokerReference,
            i.BrokerSecondaryReference, i.ErrorMessage))
        .ToList();

    return new ImportJobDetailResponse(
        job.Id, job.Broker, job.Status, job.TotalFiles,
        job.ImportedFiles, job.DuplicateFiles, job.FailedFiles,
        job.ErrorSummary, job.CreatedAt, job.StartedAt, job.CompletedAt,
        itemDetails);
}
```

### 4. Controller — two new endpoints

`BrokerPdfImportController` already receives `IBrokerPdfImportService importService` via its primary constructor. Add:

```csharp
[HttpGet("portfolios/{portfolioId:guid}/import-jobs")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<IActionResult> GetImportJobsForPortfolio(
    Guid portfolioId, CancellationToken ct = default)
{
    var userId = HttpContext.GetGuestId();
    var jobs = await importService.GetJobsByPortfolioAsync(portfolioId, userId, ct);

    return jobs is null
        ? NotFound(new { Error = $"Portfolio {portfolioId} not found or not owned by you." })
        : Ok(jobs);
}

[HttpGet("import-jobs/{jobId:guid}/items")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<IActionResult> GetImportJobDetail(
    Guid jobId, CancellationToken ct = default)
{
    var userId = HttpContext.GetGuestId();
    var detail = await importService.GetJobDetailAsync(jobId, userId, ct);

    return detail is null
        ? NotFound(new { Error = $"Import job {jobId} not found or not owned by you." })
        : Ok(detail);
}
```

### 5. Frontend — TypeScript types

Add to `frontend/src/types/index.ts` below the existing `// ── Import Jobs ───` section:

```typescript
// ── Import Archive ────────────────────────────────────────────────────────────

export interface BrokerImportJobSummary {
  jobId: string
  broker: string
  status: BrokerImportJobStatus
  totalFiles: number
  processedFiles: number
  importedFiles: number
  duplicateFiles: number
  failedFiles: number
  waitingForIngestionFiles: number
  errorSummary: string | null
  createdAt: string
  startedAt: string | null
  completedAt: string | null
}

export interface BrokerImportItemDetail {
  fileName: string
  status: BrokerImportItemStatus
  isin: string | null
  instrumentName: string | null
  resolvedTicker: string | null
  transactionType: string | null
  transactionDate: string | null
  settlementDate: string | null
  units: number | null
  pricePerUnit: number | null
  fees: number | null
  grossAmount: number | null
  currency: string | null
  brokerReference: string | null
  brokerSecondaryReference: string | null
  errorMessage: string | null
}

export interface BrokerImportJobDetail {
  jobId: string
  broker: string
  status: BrokerImportJobStatus
  totalFiles: number
  importedFiles: number
  duplicateFiles: number
  failedFiles: number
  errorSummary: string | null
  createdAt: string
  startedAt: string | null
  completedAt: string | null
  items: BrokerImportItemDetail[]
}
```

### 6. Frontend — API layer

Replace `frontend/src/api/importJobs.ts`:

```typescript
import { apiClient } from './client'
import type {
  ImportJobStatusResponse,
  BrokerImportJobSummary,
  BrokerImportJobDetail,
} from '../types'

export const importJobsApi = {
  getStatus: (jobId: string) =>
    apiClient.get<ImportJobStatusResponse>(`/import-jobs/${jobId}`),

  getByPortfolio: (portfolioId: string) =>
    apiClient.get<BrokerImportJobSummary[]>(`/portfolios/${portfolioId}/import-jobs`),

  getDetail: (jobId: string) =>
    apiClient.get<BrokerImportJobDetail>(`/import-jobs/${jobId}/items`),
}
```

### 7. Frontend — Router

Add inside the existing `children` array in `frontend/src/router/index.ts`, after the `broker-pdf-import` entry:

```typescript
{
  path: 'portfolios/:id/import-archive',
  name: 'broker-import-archive',
  component: () => import('../views/BrokerImportArchiveView.vue'),
},
{
  path: 'portfolios/:id/import-archive/:jobId',
  name: 'broker-import-job-detail',
  component: () => import('../views/BrokerImportJobDetailView.vue'),
},
```

### 8. `PortfoliosView.vue` — archive button

Add after the existing "Import PDFs" `RouterLink` in the actions row (around line 74):

```vue
<RouterLink
  v-if="store.activeId"
  :to="`/portfolios/${store.activeId}/import-archive`"
  class="text-xs px-3 py-1.5 rounded-md border border-border hover:bg-accent transition-colors">
  🗂 Import Archive
</RouterLink>
```

### 9. `BrokerImportArchiveView.vue`

```vue
<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { importJobsApi } from '@/api/importJobs'
import type { BrokerImportJobSummary, BrokerImportJobStatus } from '@/types'

const route = useRoute()
const router = useRouter()
const portfolioId = route.params.id as string

const jobs = ref<BrokerImportJobSummary[]>([])
const loading = ref(false)
const error = ref<string | null>(null)

const statusLabels: Record<string, string> = {
  queued: 'Queued',
  processing: 'Processing',
  waiting_for_ingestion: 'Waiting for market data',
  completed: 'Completed',
  completed_with_errors: 'Completed with warnings',
  failed: 'Failed',
}

function statusBadgeClass(status: BrokerImportJobStatus) {
  if (status === 'completed') return 'border-green-500/30 bg-green-500/10 text-green-600'
  if (status === 'completed_with_errors') return 'border-amber-500/30 bg-amber-500/10 text-amber-600'
  if (status === 'failed') return 'border-red-500/30 bg-red-500/10 text-red-600'
  if (status === 'waiting_for_ingestion' || status === 'processing')
    return 'border-sky-500/30 bg-sky-500/10 text-sky-600'
  return 'border-border bg-muted/50 text-muted-foreground'
}

function duration(job: BrokerImportJobSummary): string {
  if (!job.completedAt || !job.startedAt) return '—'
  const ms = new Date(job.completedAt).getTime() - new Date(job.startedAt).getTime()
  return ms < 60_000 ? `${Math.round(ms / 1000)}s` : `${Math.round(ms / 60_000)}m`
}

function openDetail(jobId: string) {
  router.push({ name: 'broker-import-job-detail', params: { id: portfolioId, jobId } })
}

onMounted(async () => {
  loading.value = true
  try {
    const { data } = await importJobsApi.getByPortfolio(portfolioId)
    jobs.value = data
  } catch {
    error.value = 'Failed to load import history.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="mx-auto max-w-5xl space-y-6">
    <div class="flex items-center justify-between">
      <div>
        <p class="text-xs font-semibold uppercase tracking-[0.2em] text-muted-foreground">
          Portfolio import
        </p>
        <h2 class="mt-2 text-3xl font-bold tracking-tight">Import Archive</h2>
      </div>
      <RouterLink
        :to="`/portfolios/${portfolioId}`"
        class="text-sm text-muted-foreground hover:text-foreground transition-colors">
        ← Back to portfolio
      </RouterLink>
    </div>

    <div v-if="loading" class="space-y-2">
      <div v-for="n in 3" :key="n" class="h-12 rounded-lg bg-muted animate-pulse" />
    </div>

    <p v-else-if="error" class="text-sm text-red-600">{{ error }}</p>

    <p v-else-if="jobs.length === 0" class="text-sm text-muted-foreground">
      No import sessions found for this portfolio.
    </p>

    <div v-else class="rounded-xl border border-border bg-card overflow-hidden">
      <table class="w-full text-sm">
        <thead class="border-b border-border bg-muted/30">
          <tr>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-[0.15em] text-muted-foreground">Date</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-[0.15em] text-muted-foreground">Broker</th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-[0.15em] text-muted-foreground">Status</th>
            <th class="px-4 py-3 text-right text-xs font-semibold uppercase tracking-[0.15em] text-muted-foreground">Total</th>
            <th class="px-4 py-3 text-right text-xs font-semibold uppercase tracking-[0.15em] text-muted-foreground">Imported</th>
            <th class="px-4 py-3 text-right text-xs font-semibold uppercase tracking-[0.15em] text-muted-foreground">Duplicates</th>
            <th class="px-4 py-3 text-right text-xs font-semibold uppercase tracking-[0.15em] text-muted-foreground">Failed</th>
            <th class="px-4 py-3 text-right text-xs font-semibold uppercase tracking-[0.15em] text-muted-foreground">Waiting</th>
            <th class="px-4 py-3 text-right text-xs font-semibold uppercase tracking-[0.15em] text-muted-foreground">Duration</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-border">
          <tr
            v-for="job in jobs"
            :key="job.jobId"
            class="cursor-pointer hover:bg-muted/30 transition-colors"
            @click="openDetail(job.jobId)"
          >
            <td class="px-4 py-3 text-foreground">{{ new Date(job.createdAt).toLocaleString() }}</td>
            <td class="px-4 py-3 text-muted-foreground capitalize">{{ job.broker.replace(/_/g, ' ') }}</td>
            <td class="px-4 py-3">
              <span
                class="inline-flex rounded-full border px-2 py-0.5 text-xs font-semibold"
                :class="statusBadgeClass(job.status)"
              >
                {{ statusLabels[job.status] ?? job.status }}
              </span>
            </td>
            <td class="px-4 py-3 text-right tabular-nums">{{ job.totalFiles }}</td>
            <td class="px-4 py-3 text-right tabular-nums text-green-600">{{ job.importedFiles }}</td>
            <td class="px-4 py-3 text-right tabular-nums text-amber-600">{{ job.duplicateFiles }}</td>
            <td class="px-4 py-3 text-right tabular-nums text-red-600">{{ job.failedFiles }}</td>
            <td class="px-4 py-3 text-right tabular-nums text-sky-600">{{ job.waitingForIngestionFiles }}</td>
            <td class="px-4 py-3 text-right tabular-nums text-muted-foreground">{{ duration(job) }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
```

### 10. `BrokerImportJobDetailView.vue`

```vue
<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { importJobsApi } from '@/api/importJobs'
import type { BrokerImportJobDetail, BrokerImportJobStatus, BrokerImportItemStatus } from '@/types'

const route = useRoute()
const portfolioId = route.params.id as string
const jobId = route.params.jobId as string

const detail = ref<BrokerImportJobDetail | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)

const statusLabels: Record<string, string> = {
  queued: 'Queued', processing: 'Processing',
  waiting_for_ingestion: 'Waiting for market data',
  completed: 'Completed', completed_with_errors: 'Completed with warnings',
  failed: 'Failed', parsing: 'Parsing', parsed: 'Parsed',
  duplicate: 'Duplicate', unsupported: 'Unsupported',
  unresolved_instrument: 'Unresolved instrument', imported: 'Imported',
}

function statusBadgeClass(status: BrokerImportJobStatus | BrokerImportItemStatus | string) {
  if (status === 'completed' || status === 'imported')
    return 'border-green-500/30 bg-green-500/10 text-green-600'
  if (['completed_with_errors', 'duplicate', 'unsupported'].includes(status))
    return 'border-amber-500/30 bg-amber-500/10 text-amber-600'
  if (status === 'failed' || status === 'unresolved_instrument')
    return 'border-red-500/30 bg-red-500/10 text-red-600'
  if (['waiting_for_ingestion', 'processing', 'parsing'].includes(status))
    return 'border-sky-500/30 bg-sky-500/10 text-sky-600'
  return 'border-border bg-muted/50 text-muted-foreground'
}

const fmt = (v: number | null, decimals = 4) =>
  v == null ? '—' : v.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: decimals })

onMounted(async () => {
  loading.value = true
  try {
    const { data } = await importJobsApi.getDetail(jobId)
    detail.value = data
  } catch {
    error.value = 'Failed to load job details.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-6">
    <RouterLink
      :to="`/portfolios/${portfolioId}/import-archive`"
      class="text-sm text-muted-foreground hover:text-foreground transition-colors">
      ← Back to archive
    </RouterLink>

    <div v-if="loading" class="space-y-3">
      <div class="h-24 rounded-xl bg-muted animate-pulse" />
      <div class="h-64 rounded-xl bg-muted animate-pulse" />
    </div>

    <p v-else-if="error" class="text-sm text-red-600">{{ error }}</p>

    <template v-else-if="detail">
      <div class="rounded-xl border border-border bg-card p-5 space-y-4">
        <div class="flex items-start justify-between gap-4">
          <div>
            <p class="text-xs font-semibold uppercase tracking-[0.2em] text-muted-foreground">
              Import session
            </p>
            <h2 class="mt-1 text-2xl font-bold tracking-tight capitalize">
              {{ detail.broker.replace(/_/g, ' ') }}
            </h2>
            <p class="mt-1 text-sm text-muted-foreground">
              Started {{ detail.startedAt ? new Date(detail.startedAt).toLocaleString() : '—' }}
              · Completed {{ detail.completedAt ? new Date(detail.completedAt).toLocaleString() : '—' }}
            </p>
          </div>
          <span
            class="inline-flex rounded-full border px-3 py-1 text-xs font-semibold"
            :class="statusBadgeClass(detail.status)"
          >
            {{ statusLabels[detail.status] ?? detail.status }}
          </span>
        </div>

        <div class="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-5">
          <div class="rounded-lg border border-border bg-muted/30 p-3">
            <p class="text-xs uppercase tracking-[0.18em] text-muted-foreground">Total</p>
            <p class="mt-1 text-lg font-semibold">{{ detail.totalFiles }}</p>
          </div>
          <div class="rounded-lg border border-border bg-muted/30 p-3">
            <p class="text-xs uppercase tracking-[0.18em] text-muted-foreground">Imported</p>
            <p class="mt-1 text-lg font-semibold text-green-600">{{ detail.importedFiles }}</p>
          </div>
          <div class="rounded-lg border border-border bg-muted/30 p-3">
            <p class="text-xs uppercase tracking-[0.18em] text-muted-foreground">Duplicates</p>
            <p class="mt-1 text-lg font-semibold text-amber-600">{{ detail.duplicateFiles }}</p>
          </div>
          <div class="rounded-lg border border-border bg-muted/30 p-3">
            <p class="text-xs uppercase tracking-[0.18em] text-muted-foreground">Failed</p>
            <p class="mt-1 text-lg font-semibold text-red-600">{{ detail.failedFiles }}</p>
          </div>
          <div class="rounded-lg border border-border bg-muted/30 p-3">
            <p class="text-xs uppercase tracking-[0.18em] text-muted-foreground">Items</p>
            <p class="mt-1 text-lg font-semibold">{{ detail.items.length }}</p>
          </div>
        </div>

        <p v-if="detail.errorSummary" class="text-sm text-red-600">{{ detail.errorSummary }}</p>
      </div>

      <div class="rounded-xl border border-border bg-card overflow-x-auto">
        <table class="w-full text-sm">
          <thead class="border-b border-border bg-muted/30">
            <tr>
              <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-[0.15em] text-muted-foreground">File</th>
              <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-[0.15em] text-muted-foreground">ISIN</th>
              <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-[0.15em] text-muted-foreground">Instrument</th>
              <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-[0.15em] text-muted-foreground">Ticker</th>
              <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-[0.15em] text-muted-foreground">Type</th>
              <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-[0.15em] text-muted-foreground">Date</th>
              <th class="px-4 py-3 text-right text-xs font-semibold uppercase tracking-[0.15em] text-muted-foreground">Units</th>
              <th class="px-4 py-3 text-right text-xs font-semibold uppercase tracking-[0.15em] text-muted-foreground">Price</th>
              <th class="px-4 py-3 text-right text-xs font-semibold uppercase tracking-[0.15em] text-muted-foreground">Fees</th>
              <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-[0.15em] text-muted-foreground">Status</th>
              <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-[0.15em] text-muted-foreground">Error</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-border">
            <tr
              v-for="(item, i) in detail.items"
              :key="i"
              class="hover:bg-muted/20 transition-colors"
            >
              <td class="px-4 py-3 max-w-[180px] truncate text-foreground" :title="item.fileName">{{ item.fileName }}</td>
              <td class="px-4 py-3 font-mono text-xs text-muted-foreground">{{ item.isin ?? '—' }}</td>
              <td class="px-4 py-3 max-w-[160px] truncate text-muted-foreground" :title="item.instrumentName ?? ''">{{ item.instrumentName ?? '—' }}</td>
              <td class="px-4 py-3 font-mono text-xs">{{ item.resolvedTicker ?? '—' }}</td>
              <td class="px-4 py-3 text-muted-foreground">{{ item.transactionType ?? '—' }}</td>
              <td class="px-4 py-3 text-muted-foreground tabular-nums">{{ item.transactionDate ?? '—' }}</td>
              <td class="px-4 py-3 text-right tabular-nums">{{ fmt(item.units, 8) }}</td>
              <td class="px-4 py-3 text-right tabular-nums">{{ fmt(item.pricePerUnit, 4) }}</td>
              <td class="px-4 py-3 text-right tabular-nums">{{ fmt(item.fees, 2) }}</td>
              <td class="px-4 py-3">
                <span
                  class="inline-flex rounded-full border px-2 py-0.5 text-xs font-semibold"
                  :class="statusBadgeClass(item.status)"
                >
                  {{ statusLabels[item.status] ?? item.status }}
                </span>
              </td>
              <td
                class="px-4 py-3 max-w-[220px] truncate text-xs text-red-600"
                :title="item.errorMessage ?? ''"
              >
                {{ item.errorMessage ?? '—' }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </div>
</template>
```

---

## Schema / Type Changes

No database migration required. All columns already exist in `broker_import_jobs` and `broker_import_job_items` as defined in `09_broker_pdf_import.sql`.

### New C# types (net additions only)

| File | Type | Notes |
|---|---|---|
| `ImportJobSummaryResponse.cs` | `record` | 13 fields; job-list shape |
| `ImportJobItemDetail.cs` | `record` | 16 fields; does not expose `temp_file_path` |
| `ImportJobDetailResponse.cs` | `record` | Job header + `IReadOnlyList<ImportJobItemDetail>` |

### New TypeScript types (net additions to `types/index.ts`)

| Name | Notes |
|---|---|
| `BrokerImportJobSummary` | Archive list row |
| `BrokerImportItemDetail` | Detail view item row (richer than `ImportJobItemResult`) |
| `BrokerImportJobDetail` | Detail view response |

`ImportJobItemResult` and `ImportJobStatusResponse` are not changed.

---

## Migration Strategy

None required.

---

## Considerations & Trade-offs

**Optimises for:** zero new architectural concepts; full reuse of the Dapper repository pattern, controller → service delegation, typed TS API client, dedicated view-per-route, and the existing badge colour logic from `BrokerPdfImportView.vue`.

**Sacrifices:**

- **No pagination.** The job list returns all rows `ORDER BY created_at DESC`. Acceptable for V1. To add pagination later: add `LIMIT @Limit OFFSET @Offset` to the repository query and `page`/`pageSize` query parameters to the endpoint — no other layer changes needed.
- **Status badge logic is duplicated** across `BrokerPdfImportView`, `BrokerImportArchiveView`, and `BrokerImportJobDetailView`. Extracting it to a `useImportStatusBadge` composable is a clean-up task, not a blocker for this feature.
- **`<tr>` is not a valid `RouterLink` target.** The archive table uses `@click` + `router.push()` on the `<tr>` element. This is the standard practice for clickable table rows in Vue.

---

## Todo List

- [ ] Phase 1: Backend DTOs
  - [ ] 1.1: Create `src/EtfInsight.Core/DTOs/ImportJobSummaryResponse.cs`
  - [ ] 1.2: Create `src/EtfInsight.Core/DTOs/ImportJobItemDetail.cs`
  - [ ] 1.3: Create `src/EtfInsight.Core/DTOs/ImportJobDetailResponse.cs`

- [ ] Phase 2: Repository
  - [ ] 2.1: Add `GetJobsByPortfolioAsync` signature to `IBrokerImportRepository`
  - [ ] 2.2: Implement `GetJobsByPortfolioAsync` in `DapperBrokerImportRepository` (column aliases must match existing `GetJobAsync`; `ORDER BY created_at DESC`)

- [ ] Phase 3: Service
  - [ ] 3.1: Add `GetJobsByPortfolioAsync` and `GetJobDetailAsync` to `IBrokerPdfImportService`
  - [ ] 3.2: Implement `GetJobsByPortfolioAsync` in `BrokerPdfImportService` (portfolio ownership check via `portfolioRepository.GetByIdAndUserAsync`; return `null` on not-found)
  - [ ] 3.3: Implement `GetJobDetailAsync` in `BrokerPdfImportService` (ownership via `brokerImportRepository.GetJobAsync`; map all item fields to `ImportJobItemDetail`)

- [ ] Phase 4: Controller
  - [ ] 4.1: Add `GET /api/portfolios/{portfolioId}/import-jobs` to `BrokerPdfImportController`
  - [ ] 4.2: Add `GET /api/import-jobs/{jobId}/items` to `BrokerPdfImportController`

- [ ] Phase 5: Frontend types and API
  - [ ] 5.1: Add `BrokerImportJobSummary` to `frontend/src/types/index.ts`
  - [ ] 5.2: Add `BrokerImportItemDetail` to `frontend/src/types/index.ts`
  - [ ] 5.3: Add `BrokerImportJobDetail` to `frontend/src/types/index.ts`
  - [ ] 5.4: Add `getByPortfolio` to `frontend/src/api/importJobs.ts`
  - [ ] 5.5: Add `getDetail` to `frontend/src/api/importJobs.ts`

- [ ] Phase 6: Routing and entry point
  - [ ] 6.1: Add `broker-import-archive` route (`/portfolios/:id/import-archive`) to `frontend/src/router/index.ts`
  - [ ] 6.2: Add `broker-import-job-detail` route (`/portfolios/:id/import-archive/:jobId`) to `frontend/src/router/index.ts`
  - [ ] 6.3: Add "Import Archive" `RouterLink` to `PortfoliosView.vue` actions row

- [ ] Phase 7: Archive list view
  - [ ] 7.1: Create `frontend/src/views/BrokerImportArchiveView.vue`
  - [ ] 7.2: Implement `onMounted` fetch via `importJobsApi.getByPortfolio`
  - [ ] 7.3: Loading skeleton (3 placeholder rows)
  - [ ] 7.4: Empty state message
  - [ ] 7.5: Jobs table (Date, Broker, Status, Total, Imported, Duplicates, Failed, Waiting, Duration)
  - [ ] 7.6: Row `@click` navigates to detail via `router.push`
  - [ ] 7.7: Status badge colour logic

- [ ] Phase 8: Job detail view
  - [ ] 8.1: Create `frontend/src/views/BrokerImportJobDetailView.vue`
  - [ ] 8.2: Implement `onMounted` fetch via `importJobsApi.getDetail`
  - [ ] 8.3: Job header card (broker name, status badge, started/completed timestamps, 5 counter tiles)
  - [ ] 8.4: "← Back to archive" `RouterLink`
  - [ ] 8.5: Items table (File, ISIN, Instrument, Ticker, Type, Date, Units, Price, Fees, Status, Error)
  - [ ] 8.6: Null fields displayed as `—`
  - [ ] 8.7: Status badge colour logic (covers both job-level and item-level statuses)

---

Do not implement yet.

