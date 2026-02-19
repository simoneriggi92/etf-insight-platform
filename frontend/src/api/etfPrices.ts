import { apiClient } from './client'
import type { EtfPrice } from '../types'

export const etfPricesApi = {
  getByTicker: (ticker: string) =>
    apiClient.get<EtfPrice[]>(`/etf-prices/${ticker}`),

  getAll: () =>
    apiClient.get<EtfPrice[]>('/etf-prices'),
}