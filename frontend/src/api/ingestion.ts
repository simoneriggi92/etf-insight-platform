import { apiClient } from './client'

export interface IngestionStatus {
  ticker: string
  status: 'unknown' | 'pending' | 'ingesting' | 'ready' | 'error'
  requestedAt: string | null
  completedAt: string | null
  error: string | null
}

export const ingestionApi = {
  getStatus: (ticker: string) =>
    apiClient.get<IngestionStatus>(`/ingestion/${ticker}/status`),
}