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
        <p class="text-xs font-semibold uppercase tracking-[0.2em] text-muted-foreground">Portfolio import</p>
        <h2 class="mt-2 text-3xl font-bold tracking-tight">Import Archive</h2>
      </div>
      <RouterLink :to="`/portfolios/${portfolioId}`"
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
        <tr v-for="job in jobs" :key="job.jobId"
            class="cursor-pointer hover:bg-muted/30 transition-colors"
            @click="openDetail(job.jobId)">
          <td class="px-4 py-3 text-foreground">{{ new Date(job.createdAt).toLocaleString() }}</td>
          <td class="px-4 py-3 text-muted-foreground capitalize">{{ job.broker.replace(/_/g, ' ') }}</td>
          <td class="px-4 py-3">
              <span class="inline-flex rounded-full border px-2 py-0.5 text-xs font-semibold"
                    :class="statusBadgeClass(job.status)">
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