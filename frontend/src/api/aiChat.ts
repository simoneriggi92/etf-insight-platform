import { apiClient } from './client'
import type { AiQueryRequest, AiQueryResponse, AiSource } from '../types'
import { useGuestSession } from '../composables/useGuestSession'

type StreamEvent =
  | { type: 'token';   value: string }
  | { type: 'sources'; value: AiSource[] }

export const aiApi = {
  query: (payload: AiQueryRequest) =>
    apiClient.post<AiQueryResponse>('/chat', payload),

  getSuggestions: () =>
    apiClient.get<{ suggestions: string[] }>('/chat/suggestions'),

  async *streamQuery(payload: AiQueryRequest): AsyncGenerator<StreamEvent> {
    const { guestId } = useGuestSession()
    const response = await fetch('/api/chat/stream', {
      method:  'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Guest-Id':   guestId.value,
      },
      body: JSON.stringify(payload),
    })

    if (!response.ok)   throw new Error(`Stream request failed: ${response.status}`)
    if (!response.body) throw new Error('Response body is null')

    const reader  = response.body.getReader()
    const decoder = new TextDecoder()
    let   buffer  = ''
    let   eventType = 'message'

    while (true) {
      const { done, value } = await reader.read()
      if (done) break

      buffer += decoder.decode(value, { stream: true })
      const lines = buffer.split('\n')
      buffer = lines.pop() ?? ''

      for (const line of lines) {
        if (line.startsWith('event: ')) {
          eventType = line.slice(7).trim()
          continue
        }
        if (!line.startsWith('data: ')) {
          if (line === '') eventType = 'message'  // blank line resets event type
          continue
        }
        const data = line.slice(6).trim()
        if (data === '[DONE]' || data === '[ERROR]') return
        try {
          if (eventType === 'sources') {
            yield { type: 'sources', value: JSON.parse(data) as AiSource[] }
          } else {
            yield { type: 'token', value: JSON.parse(data) as string }
          }
        } catch {
          // malformed chunk — skip
        }
        eventType = 'message'
      }
    }
  },
}

