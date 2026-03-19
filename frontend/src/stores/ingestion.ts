import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { ingestionApi } from '../api/ingestion'
import { usePortfoliosStore } from './portfolios'

export const useIngestionStore = defineStore('ingestion', () => {
  const pendingTickers = ref<string[]>([])
  const isIngesting    = computed(() => pendingTickers.value.length > 0)
  const timers         = new Map<string, ReturnType<typeof setInterval>>()

  function trackTicker(ticker: string) {
    if (timers.has(ticker)) return   // already tracking

    pendingTickers.value = [...pendingTickers.value, ticker]

    const timer = setInterval(async () => {
      try {
        const { data } = await ingestionApi.getStatus(ticker)

        if (data.status === 'ready' || data.status === 'error') {
          clearInterval(timer)
          timers.delete(ticker)
          pendingTickers.value = pendingTickers.value.filter(t => t !== ticker)

          // 4.3.3 — auto-refresh portfolio analytics when the last ticker lands
          if (data.status === 'ready' && pendingTickers.value.length === 0) {
            const portfoliosStore = usePortfoliosStore()
            if (portfoliosStore.activeId) {
              await portfoliosStore.fetchPortfolios()
              await portfoliosStore.selectPortfolio(portfoliosStore.activeId)
            }
          }
        }
      } catch {
        // network blip — keep polling
      }
    }, 3000)

    timers.set(ticker, timer)
  }

  return { pendingTickers, isIngesting, trackTicker }
})
