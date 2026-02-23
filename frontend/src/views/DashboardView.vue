<script setup lang="ts">
import { onMounted, computed } from 'vue'
import KpiCard from '../components/dashboard/KpiCard.vue'
import RecentAnomaliesTable from '../components/dashboard/RecentAnomaliesTable.vue'
import PortfolioValueChart from '../components/portfolios/PortfolioValueChart.vue'
import { useDataQualityStore } from '../stores/dataQuality'
import { usePortfoliosStore } from '../stores/portfolios'

const dqStore = useDataQualityStore()
const portStore = usePortfoliosStore()

onMounted (async () => {
  await Promise.all([
    dqStore.fetchAnomalies(),
    portStore.fetchPortfolios(),
  ])
})

const kpis = computed(() => [
  {
    label: 'Total Value',
    value: portStore.portfolios.length > 0 ? `${portStore.portfolios.length} portfolios` : '—',
    sub:   'Active portfolios',
    trend: 'neutral' as const,
  },
  {
    label: 'TWRR YTD',
    value: portStore.summary ? portStore.summary.twrrYtdPercentage : '—',
    sub:   portStore.summary
      ? `${portStore.summary.analysisPeriod.from} → ${portStore.summary.analysisPeriod.to}`
      : 'Year to date',
    trend: portStore.summary
      ? (portStore.summary.twrrYtd >= 0 ? 'up' : 'down') as 'up' | 'down'
      : 'neutral' as const,
  },
  {
    label: 'Open Anomalies',
    value: dqStore.unresolvedCount.toString(),
    sub:   dqStore.unresolvedCount > 0 ? 'Requires attention' : 'All clear',
    trend: dqStore.unresolvedCount > 0 ? 'down' as const : 'up' as const,
  },
])
</script>

<template>
  <div>
    <!-- Header -->
    <div class="mb-8">
      <h2 class="text-2xl font-bold tracking-tight">Dashboard</h2>
      <p class="text-muted-foreground text-sm mt-1">
        Platform overview · {{ new Date().toLocaleDateString('en-GB', { dateStyle: 'long' }) }}
      </p>
    </div>

    <!-- KPI cards -->
    <div class="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-8">
      <KpiCard
        v-for="kpi in kpis"
        :key="kpi.label"
        v-bind="kpi"
      />
    </div>

    <!-- Portfolio value chart -->
    <PortfolioValueChart class="mb-8" />

    <!-- Recent anomalies -->
    <RecentAnomaliesTable />
  </div>
</template>