<script setup lang="ts">
import { ref } from 'vue'
import { useRoute } from 'vue-router'
import CsvImportDropzone from '../components/portfolios/CsvImportDropzone.vue'
import { portfoliosApi } from '../api/portfolios'
import { useIngestionStore } from '../stores/ingestion'

const route          = useRoute()
const portfolioId    = route.params.id as string
const ingestionStore = useIngestionStore()

const selectedFile = ref<File | null>(null)
const loading      = ref(false)
const result       = ref<any | null>(null)
const submitError  = ref<string | null>(null)

function onFileSelected(file: File) {
  selectedFile.value = file
  result.value       = null
  submitError.value  = null
}

async function submit() {
  if (!selectedFile.value) return
  loading.value     = true
  submitError.value = null
  try {
    const { data, status } = await portfoliosApi.importCsv(portfolioId, selectedFile.value)
    result.value = { ...data, httpStatus: status }

    // register any ingesting tickers with the global store for auto-refresh
    for (const t of data.tickers ?? []) {
      if (t.status === 'ingesting') ingestionStore.trackTicker(t.ticker)
    }
  } catch (e: any) {
    submitError.value = e?.response?.data?.error ?? 'Import failed.'
  } finally {
    loading.value = false
  }
}

const statusColor = (s: string) => ({
  ready:     'text-green-400',
  ingesting: 'text-blue-400',
  pending:   'text-yellow-400',
  error:     'text-red-400',
  unknown:   'text-muted-foreground',
})[s] ?? 'text-muted-foreground'
</script>

<template>
  <div class="max-w-2xl mx-auto">
    <div class="mb-6">
      <h2 class="text-2xl font-bold tracking-tight">Import Transactions</h2>
      <p class="text-sm text-muted-foreground mt-1">
        Upload a CSV to bulk-import transactions. Unknown tickers will be ingested automatically.
      </p>
    </div>

    <CsvImportDropzone @file-selected="onFileSelected" />

    <button
      v-if="selectedFile"
      class="mt-4 w-full rounded-md bg-primary text-primary-foreground py-2 text-sm font-medium
             hover:opacity-90 disabled:opacity-50 transition-opacity"
      :disabled="loading"
      @click="submit"
    >
      {{ loading ? 'Importing…' : 'Import' }}
    </button>

    <p v-if="submitError" class="mt-3 text-sm text-red-500">{{ submitError }}</p>

    <!-- Result summary -->
    <div v-if="result" class="mt-6 rounded-lg border border-border bg-card p-4 space-y-4">
      <p class="text-sm font-semibold">
        {{ result.httpStatus === 202 ? '⏳' : '✅' }} {{ result.message }}
      </p>

      <!-- Per-ticker status -->
      <div v-if="result.tickers?.length">
        <p class="text-xs text-muted-foreground mb-2 font-medium uppercase tracking-wide">Tickers</p>
        <div class="flex flex-wrap gap-2">
          <span
            v-for="t in result.tickers" :key="t.ticker"
            class="text-xs px-2 py-0.5 rounded-full border border-border font-mono"
            :class="statusColor(t.status)"
          >
            {{ t.ticker }} · {{ t.status }}
          </span>
        </div>
      </div>

      <!-- Invalid rows -->
      <div v-if="result.invalidRows?.length">
        <p class="text-xs text-muted-foreground mb-2 font-medium uppercase tracking-wide">
          {{ result.invalidRows.length }} invalid row(s) skipped
        </p>
        <ul class="space-y-1">
          <li v-for="r in result.invalidRows" :key="r.row"
            class="text-xs text-red-400">
            Row {{ r.row }}: {{ r.errors.join(' · ') }}
          </li>
        </ul>
      </div>
    </div>
  </div>
</template>