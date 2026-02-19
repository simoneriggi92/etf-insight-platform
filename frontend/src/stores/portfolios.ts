import { defineStore } from 'pinia'
import { ref } from 'vue'
import { portfoliosApi } from '../api/portfolios'
import type { Portfolio, PortfolioHolding } from '../types'

export const usePortfoliosStore = defineStore('portfolios', () => {
  const portfolios = ref<Portfolio[]>([])
  const holdings   = ref<PortfolioHolding[]>([])
  const loading    = ref(false)
  const error      = ref<string | null>(null)

  async function fetchPortfolios() {
    loading.value = true
    error.value   = null
    try {
      const { data } = await portfoliosApi.getAll()
      portfolios.value = data
    } catch (e) {
      error.value = 'Failed to load portfolios.'
      console.error(e)
    } finally {
      loading.value = false
    }
  }

  async function fetchHoldings(portfolioId: string) {
    loading.value = true
    error.value   = null
    try {
      const { data } = await portfoliosApi.getHoldings(portfolioId)
      holdings.value = data
    } catch (e) {
      error.value = 'Failed to load holdings.'
      console.error(e)
    } finally {
      loading.value = false
    }
  }

  return { portfolios, holdings, loading, error, fetchPortfolios, fetchHoldings }
})