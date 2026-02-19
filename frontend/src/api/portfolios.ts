import { apiClient } from './client'
import type { Portfolio, PortfolioHolding } from '../types'

export const portfoliosApi = {
  getAll: () =>
    apiClient.get<Portfolio[]>('/portfolios'),

  getHoldings: (portfolioId: string) =>
    apiClient.get<PortfolioHolding[]>(`/portfolios/${portfolioId}/holdings`),
}