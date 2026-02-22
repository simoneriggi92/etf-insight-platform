import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { dataQualityApi } from '../api/dataQuality'
import type { DataAnomaly, Severity } from './../types/index';

export const useDataQualityStore = defineStore('dataQuality', () => {

  // ── State ──────────────────────────────────────────────────────────────────
  const anomalies   = ref<DataAnomaly[]>([])
  const loading     = ref(false)
  const error       = ref<string | null>(null)
  const lastJobId   = ref<string | null>(null)

  // ── Filter state ───────────────────────────────────────────────────────────
  const filterSeverity = ref<Severity | 'All'>('All')
  const filterResolved = ref<boolean | 'All'>('All')
  const filterTicker   = ref<string>('')

  // ── Computed ───────────────────────────────────────────────────────────────
  const filtered = computed(() => {
    return anomalies.value.filter(a => {
      const bySeverity = filterSeverity.value === 'All' || a.severity === filterSeverity.value
      const byResolved = filterResolved.value === 'All' || a.resolved === filterResolved.value
      const byTicker   = !filterTicker.value
        || a.ticker.toLowerCase().includes(filterTicker.value.toLowerCase())
      return bySeverity && byResolved && byTicker
    }).sort((a, b) => new Date(b.detectedAt).getTime() - new Date(a.detectedAt).getTime())
  })
  
  const unresolvedCount  = computed(() => anomalies.value.filter(a => !a.resolved).length)
  const criticalCount    = computed(() => anomalies.value.filter(a => a.severity === Severity.ERROR).length)
  const highCount        = computed(() => anomalies.value.filter(a => a.severity === Severity.WARNING).length)
  const resolvedCount    = computed(() => anomalies.value.filter(a => a.resolved).length)

  const recentAnomalies = computed(() =>
    [...anomalies.value]
      .sort((a, b) => new Date(b.detectedAt).getTime() - new Date(a.detectedAt).getTime())
      .slice(0, 3)
  )

  // ── Actions ────────────────────────────────────────────────────────────────
  async function fetchAnomalies() {
    loading.value = true
    error.value   = null
    try {
      const { data } = await dataQualityApi.getAnomalies()
      anomalies.value = data.anomalies
    } catch (e) {
      error.value = 'Failed to load anomalies.'
      console.error(e)
    } finally {
      loading.value = false
    }
  }

  async function triggerScan() {
    loading.value = true
    error.value   = null
    try {
      const { data } = await dataQualityApi.enqueueScan()
      lastJobId.value = data.jobId
    } catch (e) {
      error.value = 'Failed to enqueue scan.'
      console.error(e)
    } finally {
      loading.value = false
    }
  }

  return {
    anomalies, loading, error, lastJobId,
    filterSeverity, filterResolved, filterTicker,
    filtered, unresolvedCount, criticalCount, highCount, resolvedCount,
    recentAnomalies,
    fetchAnomalies, triggerScan,
  }
})