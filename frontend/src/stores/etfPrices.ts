import { defineStore } from 'pinia'
import { ref } from 'vue'
import { etfPricesApi } from '../api/etfPrices'
import type { EtfPrice } from '../types'

export const useEtfPricesStore = defineStore('etfPrices', () => {
  const prices  = ref<EtfPrice[]>([])
  const loading = ref(false)
  const error   = ref<string | null>(null)

  async function fetchByTicker(ticker: string) {
    loading.value = true
    error.value   = null
    try {
      const { data } = await etfPricesApi.getByTicker(ticker)
      prices.value = data
    } catch (e) {
      error.value = `Failed to load prices for ${ticker}.`
      console.error(e)
    } finally {
      loading.value = false
    }
  }

  return { prices, loading, error, fetchByTicker }
})