<!-- filepath: /Users/simone/Documents/PersonalProjects/etf-insight-platform/frontend/src/views/DataQualityView.vue -->
<script setup lang="ts">
import { onMounted } from 'vue'
import { useDataQualityStore } from '../stores/dataQuality'
import ScanTriggerButton from '../components/ScanTriggerButton.vue'
import AnomaliesTable    from '../components/data-quality/AnomaliesTable.vue'

const store = useDataQualityStore()
onMounted(() => store.fetchAnomalies())
</script>

<template>
  <div>
    <!-- Header -->
    <div class="flex items-start justify-between mb-6">
      <div>
        <h2 class="text-2xl font-bold tracking-tight">Data Quality</h2>
        <p class="text-muted-foreground text-sm mt-1">
          System monitoring · anomaly detection · Hangfire jobs
        </p>
      </div>
      <ScanTriggerButton />
    </div>

    <!-- Stats row -->
    <div class="grid grid-cols-2 sm:grid-cols-4 gap-4 mb-6">
      <div class="rounded-xl border border-border bg-card p-4 flex flex-col gap-1">
        <p class="text-xs text-muted-foreground uppercase tracking-widest font-medium">Total</p>
        <p class="text-2xl font-bold text-foreground">{{ store.anomalies.length }}</p>
      </div>
      <div class="rounded-xl border border-border bg-card p-4 flex flex-col gap-1">
        <p class="text-xs text-muted-foreground uppercase tracking-widest font-medium">Critical</p>
        <p class="text-2xl font-bold" :class="store.criticalCount > 0 ? 'text-red-500' : 'text-foreground'">
          {{ store.criticalCount }}
        </p>
      </div>
      <div class="rounded-xl border border-border bg-card p-4 flex flex-col gap-1">
        <p class="text-xs text-muted-foreground uppercase tracking-widest font-medium">High</p>
        <p class="text-2xl font-bold" :class="store.highCount > 0 ? 'text-orange-400' : 'text-foreground'">
          {{ store.highCount }}
        </p>
      </div>
      <div class="rounded-xl border border-border bg-card p-4 flex flex-col gap-1">
        <p class="text-xs text-muted-foreground uppercase tracking-widest font-medium">Resolved</p>
        <p class="text-2xl font-bold text-emerald-500">{{ store.resolvedCount }}</p>
      </div>
    </div>

    <!-- Full anomalies table -->
    <AnomaliesTable />

    <!-- Hangfire link -->
    <div class="mt-4 text-right">
      <a
        href="http://localhost:5001/hangfire"
        target="_blank"
        class="text-xs text-primary hover:underline font-medium"
      >
        Open Hangfire Dashboard →
      </a>
    </div>
  </div>
</template>