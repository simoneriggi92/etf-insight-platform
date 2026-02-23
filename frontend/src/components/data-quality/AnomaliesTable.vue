<!-- filepath: /Users/simone/Documents/PersonalProjects/etf-insight-platform/frontend/src/components/data-quality/AnomaliesTable.vue -->
<script setup lang="ts">
import { useDataQualityStore } from '../../stores/dataQuality'
import type { Severity } from '../../types'

const store = useDataQualityStore()

const severities: (Severity | 'All')[] = ['All', 'ERROR', 'WARNING']

const severityClass = (s: string) => ({
  'bg-red-500/15 text-red-400':       s === 'ERROR',
  'bg-orange-500/15 text-orange-400': s === 'WARNING',
})
</script>

<template>
  <div class="rounded-xl border border-border bg-card p-6">
    <!-- Filters -->
    <div class="flex flex-wrap gap-3 mb-5">
      <!-- Ticker search -->
      <input
        v-model="store.filterTicker"
        type="text"
        placeholder="Search ticker…"
        class="text-sm bg-background border border-border rounded-md px-3 py-1.5
               text-foreground placeholder:text-muted-foreground focus:outline-none
               focus:ring-1 focus:ring-primary w-36"
      />

      <!-- Severity filter -->
      <select
        v-model="store.filterSeverity"
        class="text-sm bg-background border border-border rounded-md px-3 py-1.5
               text-foreground focus:outline-none focus:ring-1 focus:ring-primary"
      >
        <option v-for="s in severities" :key="s" :value="s">
          {{ s === 'All' ? 'All severities' : s }}
        </option>
      </select>

      <!-- Resolved filter -->
      <select
        v-model="store.filterResolved"
        class="text-sm bg-background border border-border rounded-md px-3 py-1.5
               text-foreground focus:outline-none focus:ring-1 focus:ring-primary"
      >
        <option value="All">All statuses</option>
        <option :value="false">Unresolved</option>
        <option :value="true">Resolved</option>
      </select>

      <span class="ml-auto text-xs text-muted-foreground self-center">
        {{ store.filtered.length }} result{{ store.filtered.length !== 1 ? 's' : '' }}
      </span>
    </div>

    <!-- Skeleton -->
    <div v-if="store.loading" class="flex flex-col gap-2">
      <div v-for="n in 6" :key="n" class="h-9 rounded-md bg-muted animate-pulse" />
    </div>

    <!-- Empty -->
    <p v-else-if="store.filtered.length === 0"
      class="text-sm text-muted-foreground text-center py-10">
      No anomalies match the current filters ✓
    </p>

    <!-- Table -->
    <div v-else class="overflow-x-auto">
      <table class="w-full text-sm">
        <thead>
          <tr class="text-xs text-muted-foreground border-b border-border">
            <th class="text-left pb-2 font-medium">Ticker</th>
            <th class="text-left pb-2 font-medium">Rule</th>
            <th class="text-left pb-2 font-medium">Severity</th>
            <th class="text-right pb-2 font-medium">Value</th>
            <th class="text-left pb-2 font-medium">Expected</th>
            <th class="text-left pb-2 font-medium">Message</th>
            <th class="text-left pb-2 font-medium">Status</th>
            <th class="text-left pb-2 font-medium">Detected</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="a in store.filtered" :key="a.id"
            class="border-b border-border/50 last:border-0 hover:bg-muted/30 transition-colors"
          >
            <td class="py-2.5 font-mono font-semibold text-foreground">{{ a.ticker }}</td>
            <td class="py-2.5 text-muted-foreground text-xs">{{ a.ruleName }}</td>
            <td class="py-2.5">
              <span class="px-2 py-0.5 rounded-full text-xs font-medium" :class="severityClass(a.severity)">
                {{ a.severity }}
              </span>
            </td>
            <td class="py-2.5 text-right font-mono text-xs text-muted-foreground">
              {{ a.currentValue ?? '—' }}
            </td>
            <td class="py-2.5 text-xs text-muted-foreground">{{ a.expectedRange ?? '—' }}</td>
            <td class="py-2.5 text-xs text-muted-foreground max-w-xs truncate" :title="a.message">
              {{ a.message }}
            </td>
            <td class="py-2.5">
              <span
                class="px-2 py-0.5 rounded-full text-xs font-medium"
                :class="a.resolved
                  ? 'bg-emerald-500/15 text-emerald-400'
                  : 'bg-slate-500/15 text-slate-400'"
              >
                {{ a.resolved ? 'Resolved' : 'Open' }}
              </span>
            </td>
            <td class="py-2.5 text-xs text-muted-foreground">
              {{ new Date(a.detectedAt).toLocaleString('en-GB', { dateStyle: 'short', timeStyle: 'short' }) }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>