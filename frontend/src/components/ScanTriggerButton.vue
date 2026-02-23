<script setup lang="ts">
import { useDataQualityStore } from '../stores/dataQuality'

const store = useDataQualityStore()
</script>

<template>
  <div class="flex flex-col items-start gap-2">
    <button
      :disabled="store.loading"
      class="flex items-center gap-2 px-4 py-2 rounded-lg font-semibold text-sm transition-all
             bg-indigo-600 text-white hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed"
      @click="store.triggerScan()"
    >
      <svg
        v-if="store.loading"
        class="animate-spin h-4 w-4 text-white"
        xmlns="http://www.w3.org/2000/svg"
        fill="none"
        viewBox="0 0 24 24"
      >
        <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
        <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8z" />
      </svg>
      <svg
        v-else
        xmlns="http://www.w3.org/2000/svg"
        class="h-4 w-4"
        fill="none"
        viewBox="0 0 24 24"
        stroke="currentColor"
        stroke-width="2"
      >
        <path stroke-linecap="round" stroke-linejoin="round" d="M5 3l14 9-14 9V3z" />
      </svg>
      {{ store.loading ? 'Enqueueing...' : 'Run Data Quality Scan' }}
    </button>

    <p v-if="store.lastJobId" class="text-xs text-green-600 font-mono">
      Job enqueued: <span class="font-bold">{{ store.lastJobId }}</span>
    </p>
    <p v-if="store.error" class="text-xs text-red-500">
      {{ store.error }}
    </p>
  </div>
</template>
