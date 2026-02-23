import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { portfoliosApi } from '../api/portfolios'
import type { Portfolio, PortfolioDashboardDto, PortfolioSummaryDto } from '../types'

// Default date range: YTD
const today    = () => new Date().toISOString().substring(0, 10)
const ytdStart = () => `${new Date().getFullYear()}-01-01`

export const usePortfoliosStore = defineStore('portfolios', () => {

// ── State ──────────────────────────────────────────────────────────────────
  const portfolios   = ref<Portfolio[]>([])
  const activeId     = ref<string | null>(null)
  const dashboard    = ref<PortfolioDashboardDto | null>(null)
  const summary     = ref<PortfolioSummaryDto | null>(null)
  const loading      = ref(false)
  const error        = ref<string | null>(null)
  const dateFrom     = ref<string>(ytdStart())
  const dateTo       = ref<string>(today())

   // ── Computed ───────────────────────────────────────────────────────────────
  const activePortfolio = computed(() =>
    portfolios.value.find(p => p.id === activeId.value) ?? null
  )

  const history = computed(() => dashboard.value?.history ?? [])


 // ── Actions ────────────────────────────────────────────────────────────────
  async function fetchPortfolios() {
    loading.value = true
    error.value   = null
    try {
      const { data } = await portfoliosApi.getAll()
      portfolios.value = data
      const first = data[0]
      if (first && !activeId.value) {
        activeId.value = first.id
        await Promise.all([
          fetchDashboard(first.id),
          fetchPortfolioSummary(first.id),
        ])
      }
    } catch (e) {
      error.value = 'Failed to load portfolios.'
      console.error(e)
    } finally {
      loading.value = false
    }
  }

async function fetchDashboard(id: string) {
    loading.value  = true
    error.value    = null
    dashboard.value = null
    try {
      const { data } = await portfoliosApi.getDashboard(id, dateFrom.value, dateTo.value)
      dashboard.value = data
    } catch (e) {
      error.value = 'Failed to load portfolio analytics.'
      console.error(e)
    } finally {
      loading.value = false
    }
  }

  async function fetchPortfolioSummary(id: string) {
    loading.value = true
    error.value   = null
    try{
      const { data } = await portfoliosApi.getSummary(id, dateFrom.value, dateTo.value)
      summary.value = data
    } catch (e) {
      error.value = 'Failed to load portfolio summary.'
      console.error(e)
    } finally {
      loading.value = false
    }
  }

  async function selectPortfolio(id: string) {
    activeId.value = id
    await Promise.all([
      fetchDashboard(id),
      fetchPortfolioSummary(id),
    ])
  }

  async function applyDateRange(from: string, to: string) {
    dateFrom.value = from
    dateTo.value   = to
    if (activeId.value) await Promise.all([
      fetchDashboard(activeId.value),
      fetchPortfolioSummary(activeId.value),
    ])
  }

  return {
    portfolios, activeId, dashboard, summary, loading, error,
    dateFrom, dateTo,
    activePortfolio, history,
    fetchPortfolios, fetchDashboard, fetchPortfolioSummary, selectPortfolio, applyDateRange,
  }
})