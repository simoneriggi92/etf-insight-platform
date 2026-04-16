import { apiClient } from './client'
import type { ImportJobStatusResponse } from '../types'

export const importJobsApi = {
  getStatus: (jobId: string) =>
    apiClient.get<ImportJobStatusResponse>(`/import-jobs/${jobId}`),
}