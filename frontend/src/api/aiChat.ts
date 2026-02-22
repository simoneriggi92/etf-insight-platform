import { apiClient } from './client'
import type { AiQueryRequest, AiQueryResponse } from '../types'

export const aiApi = {
  query: (payload: AiQueryRequest) =>
    apiClient.post<AiQueryResponse>('/chat', payload),
}