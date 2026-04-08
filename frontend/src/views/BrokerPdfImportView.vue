<script setup lang="ts">
import axios from 'axios'
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import BrokerImportDropzone from '@/components/portfolios/BrokerImportDropzone.vue'
import { portfoliosApi } from '@/api/portfolios'
import { useImportJobPolling } from '@/composables/useImportJobPolling'
import { usePortfoliosStore } from '@/stores/portfolios'
import type { BrokerImportJobStatus, StartBrokerImportResponse } from '@/types'

type ErrorPayload = {
  error?: string
  Error?: string
}

const route = useRoute()
const portfolioId = route.params.id as string
const portfoliosStore = usePortfoliosStore()
const polling = useImportJobPolling()

const job = polling.job
const pollingError = polling.error
const status = polling.status
const percentComplete = polling.percentComplete
const recentItems = polling.recentItems
const currentMessage = polling.currentMessage
const pendingTickers = polling.pendingTickers
const isTerminalSuccess = polling.isTerminalSuccess

const selectedFiles = ref<File[]>([])
const isSubmitting = ref(false)
const submitError = ref<string | null>(null)
const startResponse = ref<StartBrokerImportResponse | null>(null)
const hasRefreshedPortfolio = ref(false)

const statusLabels: Record<string, string> = {
  queued: 'Queued',
  processing: 'Processing',
  waiting_for_ingestion: 'Waiting for market data',
  completed: 'Completed',
  completed_with_errors: 'Completed with warnings',
  failed: 'Failed',
  parsing: 'Parsing',
  parsed: 'Parsed',
  duplicate: 'Duplicate',
  unsupported: 'Unsupported',
  unresolved_instrument: 'Unresolved instrument',
  imported: 'Imported',
}

const effectiveStatus = computed<BrokerImportJobStatus | null>(() => {
  return status.value ?? startResponse.value?.status ?? null
})

const pageStateLabel = computed(() => {
  if (isSubmitting.value) {
    return 'Uploading'
  }

  if (!effectiveStatus.value) {
    return 'Idle'
  }

  return statusLabels[effectiveStatus.value] ?? effectiveStatus.value
})

const progressBarClass = computed(() => {
  if (status.value === 'failed') {
    return 'bg-red-500'
  }

  if (status.value === 'completed_with_errors') {
    return 'bg-amber-500'
  }

  if (status.value === 'waiting_for_ingestion') {
    return 'bg-sky-500'
  }

  return 'bg-primary'
})

const summaryLine = computed(() => {
  if (!job.value) {
    return null
  }

  const segments = [
    `${job.value.importedFiles} imported`,
    `${job.value.duplicateFiles} duplicate`,
    `${job.value.failedFiles} failed`,
  ]

  if (job.value.waitingForIngestionFiles > 0) {
    segments.push(`${job.value.waitingForIngestionFiles} waiting for market data`)
  }

  return segments.join(' · ')
})

const tooManyFiles = computed(() => selectedFiles.value.length > 100)
const canSubmit = computed(() => selectedFiles.value.length > 0 && !tooManyFiles.value && !isSubmitting.value)

function readApiError(payload: ErrorPayload | null | undefined): string | null {
  return payload?.error ?? payload?.Error ?? null
}

function statusText(statusValue: string) {
  return statusLabels[statusValue] ?? statusValue.replace(/_/g, ' ')
}

function statusBadgeClass(statusValue: string) {
  if (statusValue === 'completed' || statusValue === 'imported') {
    return 'border-green-500/30 bg-green-500/10 text-green-600'
  }

  if (statusValue === 'completed_with_errors' || statusValue === 'duplicate' || statusValue === 'unsupported') {
    return 'border-amber-500/30 bg-amber-500/10 text-amber-600'
  }

  if (statusValue === 'failed' || statusValue === 'unresolved_instrument') {
    return 'border-red-500/30 bg-red-500/10 text-red-600'
  }

  if (statusValue === 'waiting_for_ingestion' || statusValue === 'processing' || statusValue === 'parsing') {
    return 'border-sky-500/30 bg-sky-500/10 text-sky-600'
  }

  return 'border-border bg-muted/50 text-muted-foreground'
}

function onFilesSelected(files: File[]) {
  selectedFiles.value = files
  submitError.value = null
  startResponse.value = null
  hasRefreshedPortfolio.value = false
  polling.reset()
}

async function submit() {
  if (!canSubmit.value) {
    return
  }

  isSubmitting.value = true
  submitError.value = null
  startResponse.value = null
  hasRefreshedPortfolio.value = false

  try {
    const { data } = await portfoliosApi.importBrokerPdf(portfolioId, selectedFiles.value)
    startResponse.value = data
    await polling.start(data.jobId)
  } catch (requestError) {
    if (axios.isAxiosError<ErrorPayload>(requestError)) {
      submitError.value = readApiError(requestError.response?.data) ?? 'Import failed.'
    } else {
      submitError.value = 'Import failed.'
    }
  } finally {
    isSubmitting.value = false
  }
}

watch(isTerminalSuccess, async isSuccess => {
  if (!isSuccess || hasRefreshedPortfolio.value) {
    return
  }

  hasRefreshedPortfolio.value = true
  await portfoliosStore.fetchPortfolios()
  await portfoliosStore.selectPortfolio(portfolioId)
})

onMounted(async () => {
  if (!portfoliosStore.portfolios.length) {
    await portfoliosStore.fetchPortfolios()
  }
})
</script>

<template>
  <div class="mx-auto max-w-4xl space-y-6">
    <div>
      <p class="text-xs font-semibold uppercase tracking-[0.2em] text-muted-foreground">
        Portfolio import
      </p>
      <h2 class="mt-2 text-3xl font-bold tracking-tight">Trade Republic PDF Import</h2>
      <p class="mt-2 max-w-2xl text-sm text-muted-foreground">
        Upload a batch of Trade Republic transaction PDFs. The backend parses each file
        asynchronously, resolves the instrument, and waits for market data when required.
      </p>
    </div>

    <div class="rounded-xl border border-border bg-card p-5">
      <BrokerImportDropzone @files-selected="onFilesSelected" />

      <div class="mt-5 flex flex-col gap-3 border-t border-border pt-4 sm:flex-row sm:items-center sm:justify-between">
        <p class="text-sm text-muted-foreground">
          <span v-if="selectedFiles.length">
            {{ selectedFiles.length }} file(s) ready for upload.
          </span>
          <span v-else>
            Select one or more PDFs to enqueue a background import job.
          </span>
        </p>

        <button
          class="rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground transition-opacity hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50"
          :disabled="!canSubmit"
          @click="submit"
        >
          {{ isSubmitting ? 'Uploading...' : 'Start import' }}
        </button>
      </div>

      <p v-if="tooManyFiles" class="mt-3 text-sm text-amber-600">
        A maximum of 100 files can be uploaded per import.
      </p>

      <p v-if="submitError" class="mt-3 text-sm text-red-600">
        {{ submitError }}
      </p>
    </div>

    <div
      v-if="startResponse && !job"
      class="rounded-xl border border-border bg-card p-4"
    >
      <div class="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <p class="text-xs font-semibold uppercase tracking-[0.2em] text-muted-foreground">
            Job queued
          </p>
          <p class="mt-1 text-sm font-medium text-foreground">
            {{ startResponse.message }}
          </p>
        </div>

        <span
          class="inline-flex rounded-full border px-3 py-1 text-xs font-semibold"
          :class="statusBadgeClass(startResponse.status)"
        >
          {{ statusText(startResponse.status) }}
        </span>
      </div>
    </div>

    <div
      v-if="job"
      class="rounded-xl border border-border bg-card p-5"
    >
      <div class="flex flex-col gap-4">
        <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <p class="text-xs font-semibold uppercase tracking-[0.2em] text-muted-foreground">
              Import status
            </p>
            <h3 class="mt-1 text-xl font-semibold text-foreground">
              {{ pageStateLabel }}
            </h3>
            <p class="mt-1 text-sm text-muted-foreground">
              {{ currentMessage ?? summaryLine ?? 'Waiting for the next status update.' }}
            </p>
          </div>

          <span
            class="inline-flex rounded-full border px-3 py-1 text-xs font-semibold"
            :class="statusBadgeClass(job.status)"
          >
            {{ statusText(job.status) }}
          </span>
        </div>

        <div class="space-y-2">
          <div class="flex items-center justify-between text-xs text-muted-foreground">
            <span>{{ percentComplete }}% complete</span>
            <span>{{ job.processedFiles }} / {{ job.totalFiles }} processed</span>
          </div>

          <div class="h-2 overflow-hidden rounded-full bg-muted">
            <div
              class="h-full transition-all duration-300"
              :class="progressBarClass"
              :style="{ width: `${percentComplete}%` }"
            />
          </div>
        </div>

        <div class="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-6">
          <div class="rounded-lg border border-border bg-muted/30 p-3">
            <p class="text-xs uppercase tracking-[0.18em] text-muted-foreground">Total</p>
            <p class="mt-1 text-lg font-semibold">{{ job.totalFiles }}</p>
          </div>

          <div class="rounded-lg border border-border bg-muted/30 p-3">
            <p class="text-xs uppercase tracking-[0.18em] text-muted-foreground">Processed</p>
            <p class="mt-1 text-lg font-semibold">{{ job.processedFiles }}</p>
          </div>

          <div class="rounded-lg border border-border bg-muted/30 p-3">
            <p class="text-xs uppercase tracking-[0.18em] text-muted-foreground">Imported</p>
            <p class="mt-1 text-lg font-semibold">{{ job.importedFiles }}</p>
          </div>

          <div class="rounded-lg border border-border bg-muted/30 p-3">
            <p class="text-xs uppercase tracking-[0.18em] text-muted-foreground">Duplicates</p>
            <p class="mt-1 text-lg font-semibold">{{ job.duplicateFiles }}</p>
          </div>

          <div class="rounded-lg border border-border bg-muted/30 p-3">
            <p class="text-xs uppercase tracking-[0.18em] text-muted-foreground">Failed</p>
            <p class="mt-1 text-lg font-semibold">{{ job.failedFiles }}</p>
          </div>

          <div class="rounded-lg border border-border bg-muted/30 p-3">
            <p class="text-xs uppercase tracking-[0.18em] text-muted-foreground">Waiting</p>
            <p class="mt-1 text-lg font-semibold">{{ job.waitingForIngestionFiles }}</p>
          </div>
        </div>

        <div
          v-if="pendingTickers.length"
          class="rounded-lg border border-border bg-muted/20 p-4"
        >
          <p class="text-xs font-semibold uppercase tracking-[0.2em] text-muted-foreground">
            Pending market data
          </p>

          <div class="mt-3 flex flex-wrap gap-2">
            <span
              v-for="pendingTicker in pendingTickers"
              :key="pendingTicker.ticker"
              class="inline-flex rounded-full border border-sky-500/30 bg-sky-500/10 px-3 py-1 text-xs font-medium text-sky-600"
            >
              {{ pendingTicker.ticker }} · {{ pendingTicker.status }}
            </span>
          </div>
        </div>

        <div>
          <div class="flex items-center justify-between gap-3">
            <p class="text-xs font-semibold uppercase tracking-[0.2em] text-muted-foreground">
              Recent results
            </p>

            <p v-if="summaryLine" class="text-xs text-muted-foreground">
              {{ summaryLine }}
            </p>
          </div>

          <ul
            v-if="recentItems.length"
            class="mt-3 space-y-3"
          >
            <li
              v-for="(item, index) in recentItems"
              :key="`${item.fileName}-${index}`"
              class="rounded-lg border border-border bg-muted/20 p-4"
            >
              <div class="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
                <div class="min-w-0">
                  <p class="truncate text-sm font-medium text-foreground">
                    {{ item.fileName }}
                  </p>

                  <p class="mt-1 text-xs text-muted-foreground">
                    {{ item.resolvedTicker ?? item.isin ?? 'Awaiting instrument details' }}
                  </p>

                  <p
                    v-if="item.errorMessage"
                    class="mt-2 text-xs text-red-600"
                  >
                    {{ item.errorMessage }}
                  </p>
                </div>

                <span
                  class="inline-flex rounded-full border px-3 py-1 text-xs font-semibold"
                  :class="statusBadgeClass(item.status)"
                >
                  {{ statusText(item.status) }}
                </span>
              </div>
            </li>
          </ul>

          <p
            v-else
            class="mt-3 text-sm text-muted-foreground"
          >
            No file-level updates yet.
          </p>
        </div>

        <p
          v-if="pollingError"
          class="text-sm text-amber-600"
        >
          {{ pollingError }}
        </p>

        <p
          v-if="hasRefreshedPortfolio"
          class="text-sm text-green-600"
        >
          Portfolio analytics were refreshed after the import reached a terminal success state.
        </p>
      </div>
    </div>
  </div>
</template>