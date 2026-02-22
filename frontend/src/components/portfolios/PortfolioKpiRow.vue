<script setup lang="ts">
import { computed } from 'vue'
import { usePortfoliosStore } from '../../stores/portfolios'

const store = usePortfoliosStore()

const fmt = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })
const pct = (v: number) => `${(v * 100).toFixed(2)}%`

const kpis = computed(() => {
  const d = store.dashboard
  return [
    {
      label: 'Total Value',
      value: d ? fmt.format(d.currentTotalValue) : '—',
      sub:   d ? `Invested: ${fmt.format(d.totalInvested)}` : '',
      trend: 'neutral' as const,
    },
    {
      label: 'Absolute P&L',
      value: d ? fmt.format(d.absolutePnL) : '—',
      sub:   d ? `Return: ${pct(d.simpleReturn)}` : '',
      trend: d ? (d.absolutePnL >= 0 ? 'up' : 'down') as 'up' | 'down' : 'neutral' as const,
    },
    {
      label: 'Max Drawdown',
      value: d ? pct(d.maxDrawdown) : '—',
      sub:   'Peak to trough',
      trend: d ? (d.maxDrawdown > -0.1 ? 'neutral' : 'down') as 'neutral' | 'down' : 'neutral' as const,
    },
  ]
})
</script>

<template>
  <div class="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-6">
    <div
      v-for="kpi in kpis"
      :key="kpi.label"
      class="rounded-xl border border-border bg-card p-5 flex flex-col gap-1"
    >
      <p class="text-xs text-muted-foreground uppercase tracking-widest font-medium">
        {{ kpi.label }}
      </p>
      <p class="text-2xl font-bold tracking-tight"
        :class="{
          'text-emerald-500': kpi.trend === 'up',
          'text-red-500':     kpi.trend === 'down',
          'text-foreground':  kpi.trend === 'neutral',
        }">
        {{ kpi.value }}
      </p>
      <p class="text-xs text-muted-foreground">{{ kpi.sub }}</p>
    </div>
  </div>
</template>