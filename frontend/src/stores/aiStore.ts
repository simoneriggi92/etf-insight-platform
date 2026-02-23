import { defineStore } from 'pinia'
import { ref } from 'vue'
import { aiApi } from '../api/aiChat'
import type { AiMessage, AiQueryRequest } from '../types'

export const useAiStore = defineStore('ai', () => {

  const messages  = ref<AiMessage[]>([])
  const loading   = ref(false)
  const error     = ref<string | null>(null)
  const isOpen    = ref(false)

  function toggle() { isOpen.value = !isOpen.value }
  function open()   { isOpen.value = true }

  async function send(payload: AiQueryRequest) {
    // Push user message immediately
    messages.value.push({
      id:        crypto.randomUUID(),
      role:      'user',
      content:   payload.question,
      timestamp: new Date().toISOString(),
    })

    loading.value = true
    error.value   = null

    try {
      const { data } = await aiApi.query(payload)
      messages.value.push({
        id:        crypto.randomUUID(),
        role:      'assistant',
        content:   data.answer,
        sources:   data.sources,
        timestamp: data.timestamp,
      })
    } catch (e) {
      error.value = 'AI Advisor is unavailable. Try again later.'
      messages.value.push({
        id:        crypto.randomUUID(),
        role:      'assistant',
        content:   error.value,
        timestamp: new Date().toISOString(),
      })
    } finally {
      loading.value = false
    }
  }

  function clear() { messages.value = [] }

  return { messages, loading, error, isOpen, toggle, open, send, clear }
})