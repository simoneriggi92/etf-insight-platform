<!-- filepath: /Users/simone/Documents/PersonalProjects/etf-insight-platform/frontend/src/views/DataQualityView.vue -->
<script setup lang="ts">
import ScanTriggerButton from '../components/ScanTriggerButton.vue'
</script>

<template>
  <div>
    <h2 class="text-2xl font-bold mb-1">Data Quality</h2>
    <p class="text-muted-foreground text-sm mb-8">System monitoring · anomaly detection · job logs</p>

    <div class="mb-6">
      <ScanTriggerButton />
    </div>

    <!-- Anomalies table placeholder -->
    <div class="rounded-xl border border-border bg-card p-6 flex items-center justify-center h-48">
      <p class="text-muted-foreground text-sm">Anomalies table · coming soon</p>
    </div>
  </div>
</template>