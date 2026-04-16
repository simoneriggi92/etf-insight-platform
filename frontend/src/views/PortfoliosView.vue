<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { onMounted, ref } from 'vue'
import { usePortfoliosStore } from '../stores/portfolios'
import PortfolioKpiRow      from '../components/portfolios/PortfolioKpiRow.vue'
import AllocationChart      from '../components/portfolios/AllocationChart.vue'
import PortfolioValueChart  from '../components/portfolios/PortfolioValueChart.vue'
import DrawdownChart        from '../components/portfolios/DrawdownChart.vue'
import HoldingsTable        from '../components/portfolios/HoldingsTable.vue'
import AddTransactionForm from '../components/portfolios/AddTransactionForm.vue'

const showAddTx = ref(false)
const store = usePortfoliosStore()

onMounted(() =>{
  store.fetchPortfolios()
})

</script>

<template>
  <div>
    <!-- Header -->
    <div class="mb-6">
      <h2 class="text-2xl font-bold tracking-tight">Portfolios</h2>
      <p class="text-muted-foreground text-sm mt-1">
        Performance · allocation · transactions
      </p>
    </div>

     <!-- Skeleton -->
    <div v-if="store.loading && store.portfolios.length === 0" class="flex flex-col gap-3">
      <div v-for="n in 3" :key="n" class="h-10 rounded-lg bg-muted animate-pulse" />
    </div>

    <p v-else-if="store.error" class="text-sm text-red-500">{{ store.error }}</p>

    <p v-else-if="store.portfolios.length === 0" class="text-sm text-muted-foreground">
      No portfolios found.
    </p>

    <template v-else>
      <!-- Tabs -->
      <div class="flex gap-2 border-b border-border overflow-x-auto">
        <button
          v-for="p in store.portfolios"
          :key="p.id"
          class="whitespace-nowrap px-4 py-2 text-sm font-medium transition-colors border-b-2 -mb-px shrink-0"
          :class="store.activeId === p.id
            ? 'border-primary text-foreground'
            : 'border-transparent text-muted-foreground hover:text-foreground'"
          @click="store.selectPortfolio(p.id)"
        >
          {{ p.name }}
        </button>
      </div>

      <!-- Date range — own row, wraps nicely on all widths -->
      <div class="flex flex-wrap items-center gap-2 mt-3 mb-6">
        <span class="text-xs text-muted-foreground">From</span>
        <input type="date" v-model="store.dateFrom"
          class="text-xs bg-card border border-border rounded px-2 py-1 text-foreground min-w-0" />
        <span class="text-muted-foreground text-xs">→</span>
        <input type="date" v-model="store.dateTo"
          class="text-xs bg-card border border-border rounded px-2 py-1 text-foreground min-w-0" />
        <button
          class="text-xs px-3 py-1 bg-primary text-primary-foreground rounded font-medium"
          @click="store.activeId && store.applyDateRange(store.dateFrom, store.dateTo)"
        >
          Apply
        </button>
      </div>

      <div class="flex justify-end gap-2 mb-4">
        <RouterLink
          v-if="store.activeId"
          :to="`/portfolios/${store.activeId}/import`"
          class="text-xs px-3 py-1.5 rounded-md border border-border hover:bg-accent transition-colors">
          📥 Import CSV
        </RouterLink>
        <RouterLink
          v-if="store.activeId"
          :to="`/portfolios/${store.activeId}/import/broker-pdf`"
          class="text-xs px-3 py-1.5 rounded-md border border-border hover:bg-accent transition-colors">
          📄 Import PDFs
        </RouterLink>
        <button
          class="text-xs px-3 py-1.5 rounded-md border border-border hover:bg-accent transition-colors"
          @click="showAddTx = !showAddTx">
          {{ showAddTx ? '✕ Cancel' : '+ Add Transaction' }}
        </button>
      </div>

      <!-- Add Transaction form (collapsible) -->
      <div v-if="showAddTx && store.activeId"
        class="mb-6 rounded-lg border border-border bg-card p-4">
        <h3 class="text-sm font-semibold mb-3">Add Transaction</h3>
        <AddTransactionForm
          :portfolio-id="store.activeId"
          @done="async () => { showAddTx = false; await store.fetchPortfolios(); if (store.activeId) store.selectPortfolio(store.activeId) }" />
      </div>

      <!-- KPIs -->
      <PortfolioKpiRow />

      <!-- Charts grid -->
      <div class="grid grid-cols-1 lg:grid-cols-2 gap-4 mb-4">
        <PortfolioValueChart />
        <AllocationChart />
      </div>

      <div class="mb-4">
        <DrawdownChart />
      </div>

      <!-- Transactions -->
      <HoldingsTable />
    </template>
  </div>
</template>