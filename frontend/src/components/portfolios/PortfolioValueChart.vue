<!-- filepath: /Users/simone/Documents/PersonalProjects/etf-insight-platform/frontend/src/components/portfolios/PortfolioValueChart.vue -->
<script setup lang="ts">
import { computed } from 'vue'
import { use } from 'echarts/core'
import { LineChart } from 'echarts/charts'
import { TooltipComponent, GridComponent, DataZoomComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import VChart from 'vue-echarts'
import { usePortfoliosStore } from '../../stores/portfolios'

use([LineChart, TooltipComponent, GridComponent, DataZoomComponent, CanvasRenderer])

const store = usePortfoliosStore()

const option = computed(() => {
  const h = store.history
  return {
    backgroundColor: 'transparent',
    tooltip: {
      trigger: 'axis',
      formatter: (params: any[]) => {
        const p = params[0]
        return `${p.name}<br/><b>$${p.value.toLocaleString()}</b>`
      },
    },
    grid: { left: 16, right: 16, top: 16, bottom: 40, containLabel: true },
    dataZoom: [{ type: 'inside' }, { type: 'slider', height: 20 }],
    xAxis: {
      type: 'category',
      data: h.map(p => p.date),
      axisLabel: { color: '#94a3b8', fontSize: 11 },
      axisLine: { lineStyle: { color: '#334155' } },
    },
    yAxis: {
      type: 'value',
      axisLabel: {
        color: '#94a3b8',
        fontSize: 11,
        formatter: (v: number) => `$${(v / 1000).toFixed(0)}k`,
      },
      splitLine: { lineStyle: { color: '#1e293b' } },
    },
    series: [{
      name: 'Portfolio Value',
      type: 'line',
      data: h.map(p => p.totalValue),
      smooth: true,
      symbol: 'none',
      lineStyle: { width: 2, color: '#6366f1' },
      areaStyle: {
        color: {
          type: 'linear', x: 0, y: 0, x2: 0, y2: 1,
          colorStops: [
            { offset: 0, color: 'rgba(99,102,241,0.25)' },
            { offset: 1, color: 'rgba(99,102,241,0)' },
          ],
        },
      },
    }],
  }
})
</script>

<template>
  <div class="rounded-xl border border-border bg-card p-6">
    <h3 class="text-sm font-semibold text-foreground mb-4">Portfolio Value</h3>
    <div v-if="store.history.length === 0"
      class="flex items-center justify-center h-52 text-muted-foreground text-sm">
      {{ store.loading ? 'Loading…' : 'No history data for selected period' }}
    </div>
    <VChart v-else :option="option" style="height: 240px" autoresize />
  </div>
</template>