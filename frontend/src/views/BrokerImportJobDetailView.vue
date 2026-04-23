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
    <RouterLink :to="`/portfolios/${portfolioId}/import-archive`"
                class="text-sm text-muted-foreground hover:text-foreground transition-colors">
      ← Back to archive
    </RouterLink>

    <div v-if="loading" class="space-y-3">
      <div class="h-24 rounded-xl bg-muted animate-pulse" />
      <div class="h-64 rounded-xl bg-muted animate-pulse" />
    </div>

    <p v-else-if="error" class="text-sm text-red-600">{{ error }}</p>

    <template v-else-if="detail">
      <!-- Job header -->
      <div class="rounded-xl border border-border bg-card p-5 space-y-4">
        <div class="flex items-start justify-between gap-4">
          <div>
            <p class="text-xs font-semibold uppercase tracking-[0.2em] text-muted-foreground">Import session</p>
            <h2 class="mt-1 text-2xl font-bold tracking-tight capitalize">
              {{ detail.broker.replace(/_/g, ' ') }}
            </h2>
            <p class="mt-1 text-sm text-muted-foreground">
              Started {{ detail.startedAt ? new Date(detail.startedAt).toLocaleString() : '—' }}
              · Completed {{ detail.completedAt ? new Date(detail.completedAt).toLocaleString() : '—' }}
            </p>
          </div>
          <span class="inline-flex rounded-full border px-3 py-1 text-xs font-semibold"
                :class="statusBadgeClass(detail.status)">
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

      <!-- Items table -->
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
          <tr v-for="(item, i) in detail.items" :key="i"
              class="hover:bg-muted/20 transition-colors">
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
                <span class="inline-flex rounded-full border px-2 py-0.5 text-xs font-semibold"
                      :class="statusBadgeClass(item.status)">
                  {{ statusLabels[item.status] ?? item.status }}
                </span>
            </td>
            <td class="px-4 py-3 max-w-[220px] truncate text-xs text-red-600" :title="item.errorMessage ?? ''">
              {{ item.errorMessage ?? '—' }}
            </td>
          </tr>
          </tbody>
        </table>
      </div>
    </template>
  </div>
</template>