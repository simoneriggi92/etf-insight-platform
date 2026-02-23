import { apiClient } from './client'
import type { DataAnomalyResponse, ScanEnqueuedResponse } from '../types'

export const dataQualityApi = {
  enqueueScan: () =>
    apiClient.post<ScanEnqueuedResponse>('/data-quality/scan'),

  getAnomalies: () =>
    apiClient.get<DataAnomalyResponse>('/data-quality/anomalies'),
}