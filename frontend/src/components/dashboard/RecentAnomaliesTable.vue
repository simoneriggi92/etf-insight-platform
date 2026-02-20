<!-- filepath: /Users/simone/Documents/PersonalProjects/etf-insight-platform/frontend/src/components/dashboard/RecentAnomaliesTable.vue -->
<script setup lang="ts">
import { useDataQualityStore } from '../../stores/dataQuality'
import { onMounted } from 'vue'

const store = useDataQualityStore()
onMounted(() => store.fetchAnomalies())

const severityClass = (severity: string) => ({
  'bg-red-500/15 text-red-400':      severity === 'ERROR',
  'bg-orange-500/15 text-orange-400': severity === 'WARNING',
})
</script>

<template>
  <div class="rounded-xl border border-border bg-card p-6">
    <h3 class="text-sm font-semibold text-foreground mb-4">Recent Anomalies</h3>

    <!-- Loading -->
    <div v-if="store.loading" class="flex flex-col gap-2">
      <div v-for="n in 3" :key="n"
        class="h-8 rounded-md bg-muted animate-pulse" />
    </div>

    <!-- Empty -->
    <p v-else-if="store.recentAnomalies.length === 0"
      class="text-sm text-muted-foreground text-center py-6">
      No anomalies detected ✓
    </p>

    <!-- Table -->
    <table v-else class="w-full text-sm">
      <thead>
        <tr class="text-xs text-muted-foreground border-b border-border">
          <th class="text-left pb-2 font-medium">Ticker</th>
          <th class="text-left pb-2 font-medium">Rule</th>
          <th class="text-left pb-2 font-medium">Severity</th>
          <th class="text-left pb-2 font-medium">Date</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="anomaly in store.recentAnomalies"
          :key="anomaly.id"
          class="border-b border-border/50 last:border-0"
        >
          <td class="py-2.5 font-mono font-semibold text-foreground">
            {{ anomaly.ticker }}
          </td>
          <td class="py-2.5 text-muted-foreground">{{ anomaly.ruleName }}</td>
          <td class="py-2.5">
            <span class="px-2 py-0.5 rounded-full text-xs font-medium"
              :class="severityClass(anomaly.severity)">
              {{ anomaly.severity }}
            </span>
          </td>
          <td class="py-2.5 text-muted-foreground text-xs">
            {{ new Date(anomaly.detectedAt).toLocaleDateString() }}
          </td>
        </tr>
      </tbody>
    </table>

    <!-- Link to full view -->
    <div class="mt-4 text-right">
      <RouterLink to="/data-quality"
        class="text-xs text-primary hover:underline font-medium">
        View all anomalies →
      </RouterLink>
    </div>
  </div>
</template>