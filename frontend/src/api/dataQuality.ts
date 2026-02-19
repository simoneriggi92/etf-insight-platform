import { apiClient } from './client'
import type { DataAnomaly, ScanEnqueuedResponse } from '../types'

export const dataQualityApi = {
  enqueueScan: () =>
    apiClient.post<ScanEnqueuedResponse>('/data-quality/scan'),

  getAnomalies: () =>
    apiClient.get<DataAnomaly[]>('/data-quality/anomalies'),
}