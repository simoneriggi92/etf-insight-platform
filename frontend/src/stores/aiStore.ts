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
    } catch {
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

  async function sendStreaming(payload: AiQueryRequest) {
    messages.value.push({
      id:        crypto.randomUUID(),
      role:      'user',
      content:   payload.question,
      timestamp: new Date().toISOString(),
    })

    const assistantMsg: AiMessage = {
      id:        crypto.randomUUID(),
      role:      'assistant',
      content:   '',
      timestamp: new Date().toISOString(),
    }
    messages.value.push(assistantMsg)
    loading.value = true
    error.value   = null

    try {
      for await (const event of aiApi.streamQuery(payload)) {
        const idx = messages.value.findIndex(m => m.id === assistantMsg.id)
        if (idx === -1) continue
        if (event.type === 'token') {
          assistantMsg.content += event.value
          messages.value[idx] = { ...assistantMsg }
        } else if (event.type === 'sources') {
          assistantMsg.sources = event.value
          messages.value[idx] = { ...assistantMsg }
        }
      }
    } catch {
      error.value = 'AI Advisor is unavailable. Try again later.'
      assistantMsg.content = error.value
      const idx = messages.value.findIndex(m => m.id === assistantMsg.id)
      if (idx !== -1) messages.value[idx] = { ...assistantMsg }
    } finally {
      loading.value = false
    }
  }

  function clear() { messages.value = [] }

  return { messages, loading, error, isOpen, toggle, open, send, sendStreaming, clear }
})