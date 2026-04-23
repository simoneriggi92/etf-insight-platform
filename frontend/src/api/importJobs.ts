import { apiClient } from './client'
import type {BrokerImportJobDetail, BrokerImportJobSummary, ImportJobStatusResponse} from '../types'

export const importJobsApi = {
  getStatus: (jobId: string) =>
      apiClient.get<ImportJobStatusResponse>(`/import-jobs/${jobId}`),

  getByPortfolio: (portfolioId: string) =>
      apiClient.get<BrokerImportJobSummary[]>(`/portfolios/${portfolioId}/import-jobs`),

  getDetail: (jobId: string) =>
      apiClient.get<BrokerImportJobDetail>(`/import-jobs/${jobId}/items`),
}