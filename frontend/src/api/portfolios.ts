import { apiClient } from './client'
import type { Portfolio, PortfolioDashboardDto, PortfolioSummaryDto } from '../types'

export interface CreatePortfolioPayload {
  name: string
  baseCurrency?: string
}

export interface AddTransactionPayload {
  ticker: string
  type: 'BUY' | 'SELL' | 'DEPOSIT' | 'WITHDRAW'
  units: number
  pricePerUnit: number
  fees?: number
  currency?: string
  transactionDate?: string    // YYYY-MM-DD
}


export const portfoliosApi = {
  getAll: () =>
    apiClient.get<Portfolio[]>('/portfolios'),

  getById: (id: string) =>
    apiClient.get<Portfolio>(`/portfolios/${id}`),

  create: (payload: CreatePortfolioPayload) =>
    apiClient.post<{ portfolio: { id: string; name: string; currency: string; created_at: string } }>(
      '/portfolios', payload),

  addTransaction: (portfolioId: string, payload: AddTransactionPayload) =>
    apiClient.post(
      `/portfolios/${portfolioId}/transactions`,
      payload,
      { validateStatus: s => s < 500 }),   // accept 202 without throwing

  getDashboard: (portfolioId: string, from?: string, to?: string) =>
    apiClient.get<PortfolioDashboardDto>(`/portfolios/${portfolioId}/analytics/dashboard`,
       { params: { from, to } }),

  getSummary: (portfolioId: string, from?: string, to?: string) =>
    apiClient.get<PortfolioSummaryDto>(`/portfolios/${portfolioId}/analytics/summary`,
       { params: { from, to } }),
       
  importCsv: (portfolioId: string, file: File) => {
  const form = new FormData()
  form.append('file', file)
  return apiClient.post(
    `/portfolios/${portfolioId}/transactions/import`,
    form,
    { headers: { 'Content-Type': 'multipart/form-data' }, validateStatus: s => s < 500 }
  )
},
}