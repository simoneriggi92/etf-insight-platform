import axios from 'axios'
import { computed, onUnmounted, ref } from 'vue'
import { importJobsApi } from '@/api/importJobs'
import type {
  BrokerImportJobStatus,
  ImportJobItemResult,
  ImportJobStatusResponse,
} from '@/types'

type ErrorPayload = {
  error?: string
  Error?: string
}

const terminalStatuses: BrokerImportJobStatus[] = [
  'completed',
  'completed_with_errors',
  'failed',
]

const terminalSuccessStatuses: BrokerImportJobStatus[] = [
  'completed',
  'completed_with_errors',
]

function readApiError(payload: ErrorPayload | null | undefined): string | null {
  return payload?.error ?? payload?.Error ?? null
}

export function useImportJobPolling(intervalMs = 2500) {
  const job = ref<ImportJobStatusResponse | null>(null)
  const error = ref<string | null>(null)
  const loading = ref(false)

  let timer: ReturnType<typeof setInterval> | null = null
  let activeJobId: string | null = null

  const status = computed(() => job.value?.status ?? null)

  const percentComplete = computed(() => {
    if (!job.value || job.value.totalFiles === 0) {
      return 0
    }

    if (status.value && terminalStatuses.includes(status.value)) {
      return 100
    }

    return Math.min(
      100,
      Math.round((job.value.processedFiles / job.value.totalFiles) * 100),
    )
  })

  const recentItems = computed<ImportJobItemResult[]>(() => job.value?.recentItems ?? [])

  const currentMessage = computed(() => job.value?.currentMessage ?? null)

  const pendingTickers = computed(() =>
    Object.entries(job.value?.tickerIngestionStatuses ?? {})
      .filter(([, tickerStatus]) => tickerStatus !== 'ready' && tickerStatus !== 'error')
      .map(([ticker, tickerStatus]) => ({
        ticker,
        status: tickerStatus,
      })),
  )

  const isTerminal = computed(() =>
    status.value ? terminalStatuses.includes(status.value) : false,
  )

  const isTerminalSuccess = computed(() =>
    status.value ? terminalSuccessStatuses.includes(status.value) : false,
  )

  async function pollOnce() {
    if (!activeJobId) {
      return
    }

    loading.value = true

    try {
      const { data } = await importJobsApi.getStatus(activeJobId)
      job.value = data
      error.value = null

      if (terminalStatuses.includes(data.status)) {
        stop()
      }
    } catch (requestError) {
      if (axios.isAxiosError<ErrorPayload>(requestError) && requestError.response?.status === 404) {
        error.value = readApiError(requestError.response.data) ?? 'Import job not found.'
        stop()
      } else {
        error.value = 'Temporary polling error. Retrying...'
      }
    } finally {
      loading.value = false
    }
  }

  async function start(jobId: string) {
    activeJobId = jobId
    stop()
    await pollOnce()

    if (!isTerminal.value) {
      timer = setInterval(() => {
        void pollOnce()
      }, intervalMs)
    }
  }

  function stop() {
    if (timer) {
      clearInterval(timer)
      timer = null
    }
  }

  function reset() {
    stop()
    activeJobId = null
    job.value = null
    error.value = null
    loading.value = false
  }

  onUnmounted(stop)

  return {
    job,
    error,
    loading,
    status,
    percentComplete,
    recentItems,
    currentMessage,
    pendingTickers,
    isTerminal,
    isTerminalSuccess,
    start,
    stop,
    reset,
    pollOnce,
  }
}