<script setup lang="ts">
import { reactive, ref } from 'vue'
import { portfoliosApi } from '@/api/portfolios'
import { useIngestionStore } from '@/stores/ingestion'

const props  = defineProps<{ portfolioId: string }>()
const emit   = defineEmits<{ (e: 'done'): void }>()

const loading = ref(false)
const submitError = ref<string | null>(null)

const form = reactive({
  ticker:          '',
  type:            'BUY' as 'BUY' | 'SELL' | 'DEPOSIT' | 'WITHDRAW',
  units:           '' as number | '',
  pricePerUnit:    '' as number | '',
  fees:            0,
  transactionDate: new Date().toISOString().slice(0, 10),
})

const ingestionStore = useIngestionStore()

async function submit() {
  submitError.value = null
  if (!form.ticker || !form.units || !form.pricePerUnit) {
    submitError.value = 'Ticker, units and price are required.'
    return
  }

  loading.value = true
  try {
    const { data, status } = await portfoliosApi.addTransaction(props.portfolioId, {
      ticker:          form.ticker.trim().toUpperCase(),
      type:            form.type,
      units:           Number(form.units),
      pricePerUnit:    Number(form.pricePerUnit),
      fees:            form.fees,
      transactionDate: form.transactionDate,
    })

    if (status === 202 && data.ingestion?.status === 'ingesting') {
      ingestionStore.trackTicker(form.ticker)   // global badge + auto-refresh when ready
    }
    emit('done')  // always close the form immediately
  } catch (e: any) {
    submitError.value = e?.response?.data?.error ?? 'Failed to add transaction.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <form class="flex flex-col gap-4" @submit.prevent="submit">

    <div class="grid grid-cols-2 gap-3">
      <div class="col-span-2">
        <label class="text-xs text-muted-foreground mb-1 block">Ticker</label>
        <input v-model="form.ticker" placeholder="VUSA.MI"
          class="w-full bg-background border border-border rounded px-3 py-2 text-sm uppercase"
          required />
      </div>

      <div>
        <label class="text-xs text-muted-foreground mb-1 block">Type</label>
        <select v-model="form.type"
          class="w-full bg-background border border-border rounded px-3 py-2 text-sm">
          <option>BUY</option>
          <option>SELL</option>
          <option>DEPOSIT</option>
          <option>WITHDRAW</option>
        </select>
      </div>

      <div>
        <label class="text-xs text-muted-foreground mb-1 block">Date</label>
        <input v-model="form.transactionDate" type="date"
          class="w-full bg-background border border-border rounded px-3 py-2 text-sm" />
      </div>

      <div>
        <label class="text-xs text-muted-foreground mb-1 block">Units</label>
        <input v-model.number="form.units" type="number" step="0.0001" min="0" placeholder="10"
          class="w-full bg-background border border-border rounded px-3 py-2 text-sm" required />
      </div>

      <div>
        <label class="text-xs text-muted-foreground mb-1 block">Price per unit</label>
        <input v-model.number="form.pricePerUnit" type="number" step="0.0001" min="0" placeholder="72.30"
          class="w-full bg-background border border-border rounded px-3 py-2 text-sm" required />
      </div>

      <div>
        <label class="text-xs text-muted-foreground mb-1 block">Fees</label>
        <input v-model.number="form.fees" type="number" step="0.01" min="0" placeholder="2.99"
          class="w-full bg-background border border-border rounded px-3 py-2 text-sm" />
      </div>
    </div>

    <!-- Error -->
    <p v-if="submitError" class="text-xs text-red-500">{{ submitError }}</p>

    <button type="submit" :disabled="loading"
      class="w-full rounded-md bg-primary text-primary-foreground py-2 text-sm font-medium
             hover:opacity-90 disabled:opacity-50 transition-opacity">
      {{ loading ? 'Saving…' : 'Add Transaction' }}

    </button>
  </form>
</template>