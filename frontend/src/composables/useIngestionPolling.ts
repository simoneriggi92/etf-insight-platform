import { ref, onUnmounted } from 'vue'
import { ingestionApi } from '@/api/ingestion'

export function useIngestionPolling(intervalMs = 3000) {
  const status  = ref<string | null>(null)
  const error   = ref<string | null>(null)
  let timer: ReturnType<typeof setInterval> | null = null

  function start(ticker: string, onReady?: () => void) {
    status.value = 'ingesting'
    stop()                           // clear any previous timer

    timer = setInterval(async () => {
      try {
        const { data } = await ingestionApi.getStatus(ticker)
        status.value = data.status
        if (data.status === 'error') error.value = data.error
        if (data.status === 'ready' || data.status === 'error') {
          stop()
          if (data.status === 'ready') onReady?.()
        }
      } catch {
        // network blip — keep polling
      }
    }, intervalMs)
  }

  function stop() {
    if (timer) { clearInterval(timer); timer = null }
  }

  onUnmounted(stop)

  return { status, error, start, stop }
}