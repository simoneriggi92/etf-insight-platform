<!-- filepath: /Users/simone/Documents/PersonalProjects/etf-insight-platform/frontend/src/components/portfolios/AllocationChart.vue -->
<script setup lang="ts">
import { computed } from 'vue'
import { use } from 'echarts/core'
import { PieChart } from 'echarts/charts'
import { TooltipComponent, LegendComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import VChart from 'vue-echarts'
import { usePortfoliosStore } from '../../stores/portfolios'

use([PieChart, TooltipComponent, LegendComponent, CanvasRenderer])

const store = usePortfoliosStore()

// Aggregate BUY transactions by ticker to compute allocation
const allocation = computed(() => {
  const txns = store.activePortfolio?.transactions ?? []
  const map: Record<string, number> = {}
  for (const t of txns) {
    if (t.type === 'BUY') {
      map[t.ticker] = (map[t.ticker] ?? 0) + t.units * t.pricePerUnit
    }
    if (t.type === 'SELL') {
      map[t.ticker] = (map[t.ticker] ?? 0) - t.units * t.pricePerUnit
    }
  }
  return Object.entries(map)
    .filter(([, v]) => v > 0)
    .map(([name, value]) => ({ name, value: +value.toFixed(2) }))
})

const option = computed(() => ({
  backgroundColor: 'transparent',
  tooltip: { trigger: 'item', formatter: '{b}: ${c} ({d}%)' },
  legend: {
    orient: 'vertical',
    right: 10,
    top: 'center',
    textStyle: { color: '#94a3b8', fontSize: 12 },
  },
  series: [{
    type: 'pie',
    radius: ['45%', '72%'],
    center: ['38%', '50%'],
    padAngle: 3,
    itemStyle: { borderRadius: 6 },
    label: { show: false },
    emphasis: { label: { show: true, fontSize: 13, fontWeight: 'bold' } },
    data: allocation.value,
  }],
}))
</script>

<template>
  <div class="rounded-xl border border-border bg-card p-6">
    <h3 class="text-sm font-semibold text-foreground mb-4">Asset Allocation</h3>
    <div v-if="allocation.length === 0"
      class="flex items-center justify-center h-52 text-muted-foreground text-sm">
      No transaction data
    </div>
    <VChart v-else :option="option" style="height: 240px" autoresize />
  </div>
</template>