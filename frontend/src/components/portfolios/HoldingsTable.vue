<!-- filepath: /Users/simone/Documents/PersonalProjects/etf-insight-platform/frontend/src/components/portfolios/HoldingsTable.vue -->
<script setup lang="ts">
import { computed } from 'vue'
import { usePortfoliosStore } from '../../stores/portfolios'
import type { TransactionType } from '../../types'

const store = usePortfoliosStore()

const fmt = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

const rows = computed(() =>
  [...(store.activePortfolio?.transactions ?? [])]
    .sort((a, b) => new Date(b.transactionDate).getTime() - new Date(a.transactionDate).getTime())
)

const typeBadge = (type: TransactionType) => ({
  'bg-emerald-500/15 text-emerald-400': type === 'BUY'      || type === 'DEPOSIT',
  'bg-red-500/15 text-red-400':         type === 'SELL'     || type === 'WITHDRAW',
})
</script>

<template>
  <div class="rounded-xl border border-border bg-card p-6">
    <h3 class="text-sm font-semibold text-foreground mb-4">Transactions</h3>

    <div v-if="store.loading" class="flex flex-col gap-2">
      <div v-for="n in 5" :key="n" class="h-8 rounded-md bg-muted animate-pulse" />
    </div>

    <p v-else-if="rows.length === 0"
      class="text-sm text-muted-foreground text-center py-8">
      No transactions
    </p>

    <div v-else class="overflow-x-auto">
      <table class="w-full text-sm">
        <thead>
          <tr class="text-xs text-muted-foreground border-b border-border">
            <th class="text-left pb-2 font-medium">Date</th>
            <th class="text-left pb-2 font-medium">Type</th>
            <th class="text-left pb-2 font-medium">Ticker</th>
            <th class="text-right pb-2 font-medium">Units</th>
            <th class="text-right pb-2 font-medium">Price/Unit</th>
            <th class="text-right pb-2 font-medium">Total</th>
            <th class="text-right pb-2 font-medium">Fees</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="t in rows" :key="t.id"
            class="border-b border-border/50 last:border-0 hover:bg-muted/30 transition-colors"
          >
            <td class="py-2.5 text-muted-foreground text-xs">{{ t.transactionDate }}</td>
            <td class="py-2.5">
              <span class="px-2 py-0.5 rounded-full text-xs font-medium" :class="typeBadge(t.type)">
                {{ t.type }}
              </span>
            </td>
            <td class="py-2.5 font-mono font-semibold text-foreground">{{ t.ticker }}</td>
            <td class="py-2.5 text-right text-muted-foreground">{{ t.units }}</td>
            <td class="py-2.5 text-right text-muted-foreground">{{ fmt.format(t.pricePerUnit) }}</td>
            <td class="py-2.5 text-right font-medium text-foreground">
              {{ fmt.format(t.units * t.pricePerUnit) }}
            </td>
            <td class="py-2.5 text-right text-muted-foreground text-xs">
              {{ fmt.format(t.fees) }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>