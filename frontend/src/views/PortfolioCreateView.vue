<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { portfoliosApi } from '@/api/portfolios'
import { usePortfoliosStore } from '@/stores/portfolios'

const router  = useRouter()
const store   = usePortfoliosStore()
const loading = ref(false)
const error   = ref<string | null>(null)

const form = reactive({ name: '', baseCurrency: 'EUR' })

async function submit() {
  if (!form.name.trim()) return
  loading.value = true
  error.value   = null
  try {
    const { data } = await portfoliosApi.create({ name: form.name, baseCurrency: form.baseCurrency })
    await store.fetchPortfolios()                        // refresh store
    router.push(`/portfolios/${data.portfolio.id}`)
  } catch (e: any) {
    error.value = e?.response?.data?.error ?? 'Failed to create portfolio.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="max-w-md mx-auto mt-10">
    <h2 class="text-2xl font-bold mb-1">New Portfolio</h2>
    <p class="text-sm text-muted-foreground mb-6">Give your portfolio a name and base currency.</p>

    <form class="flex flex-col gap-4" @submit.prevent="submit">
      <div>
        <label class="text-xs text-muted-foreground mb-1 block">Portfolio name</label>
        <input v-model="form.name" placeholder="My Growth Portfolio"
          class="w-full bg-background border border-border rounded px-3 py-2 text-sm"
          required />
      </div>

      <div>
        <label class="text-xs text-muted-foreground mb-1 block">Base currency</label>
        <select v-model="form.baseCurrency"
          class="w-full bg-background border border-border rounded px-3 py-2 text-sm">
          <option>EUR</option>
          <option>USD</option>
          <option>GBP</option>
        </select>
      </div>

      <p v-if="error" class="text-xs text-red-500">{{ error }}</p>

      <button type="submit" :disabled="loading"
        class="w-full rounded-md bg-primary text-primary-foreground py-2 text-sm font-medium
               hover:opacity-90 disabled:opacity-50 transition-opacity">
        {{ loading ? 'Creating…' : 'Create Portfolio' }}
      </button>
    </form>
  </div>
</template>