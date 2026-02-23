import { apiClient } from './client'
import type { Portfolio, PortfolioDashboardDto, PortfolioSummaryDto } from '../types'

export const portfoliosApi = {
  getAll: () =>
    apiClient.get<Portfolio[]>('/portfolios'),

  getById: (id: string) =>
    apiClient.get<Portfolio>(`/portfolios/${id}`),

  getDashboard: (portfolioId: string, from?: string, to?: string) =>
    apiClient.get<PortfolioDashboardDto>(`/portfolios/${portfolioId}/analytics/dashboard`,
       { params: { from, to } }),

  getSummary: (portfolioId: string, from?: string, to?: string) =>
    apiClient.get<PortfolioSummaryDto>(`/portfolios/${portfolioId}/analytics/summary`,
       { params: { from, to } }),
}