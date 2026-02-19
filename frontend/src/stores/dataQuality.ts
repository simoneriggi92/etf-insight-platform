import { defineStore } from 'pinia'
import { ref } from 'vue'
import { dataQualityApi } from '../api/dataQuality'
import type { DataAnomaly } from '../types'

export const useDataQualityStore = defineStore('dataQuality', () => {
  const anomalies = ref<DataAnomaly[]>([])
  const loading   = ref(false)
  const error     = ref<string | null>(null)
  const lastJobId = ref<string | null>(null)

  async function fetchAnomalies() {
    loading.value = true
    error.value   = null
    try {
      const { data } = await dataQualityApi.getAnomalies()
      anomalies.value = data
    } catch (e) {
      error.value = 'Failed to load anomalies.'
      console.error(e)
    } finally {
      loading.value = false
    }
  }

  async function triggerScan() {
    try {
      const { data } = await dataQualityApi.enqueueScan()
      lastJobId.value = data.jobId
    } catch (e) {
      error.value = 'Failed to enqueue scan.'
      console.error(e)
    }
  }

  return { anomalies, loading, error, lastJobId, fetchAnomalies, triggerScan }
})