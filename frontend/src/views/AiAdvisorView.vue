<script setup lang="ts">
import { ref, nextTick, watch } from 'vue'
import { useAiStore } from '../stores/aiStore'
import { useAiSuggestions } from '../composables/useAiSuggestions'
import MessageBubble from '../components/ai-advisor/MessageBubble.vue'

const ai = useAiStore()
const { suggestions } = useAiSuggestions()

const input    = ref('')
const scrollEl = ref<HTMLElement>()

async function send() {
  const q = input.value.trim()
  if (!q || ai.loading) return
  input.value = ''
  await ai.sendStreaming({ question: q })
  await nextTick()
  scrollEl.value?.scrollTo({ top: scrollEl.value.scrollHeight, behavior: 'smooth' })
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send() }
}

watch(() => ai.messages.length, async () => {
  await nextTick()
  scrollEl.value?.scrollTo({ top: scrollEl.value.scrollHeight, behavior: 'smooth' })
})
</script>

<template>
  <div class="flex flex-col h-full max-w-3xl mx-auto">
    <div class="mb-6">
      <h2 class="text-2xl font-bold mb-1">AI Advisor</h2>
      <p class="text-muted-foreground text-sm">RAG · pgvector · llama3.2</p>
    </div>

    <!-- Chat area -->
    <div
      ref="scrollEl"
      class="flex-1 overflow-y-auto rounded-xl border border-border bg-card p-6
             flex flex-col gap-4 min-h-[400px] max-h-[60vh]"
    >
      <!-- Welcome state -->
      <div
        v-if="ai.messages.length === 0"
        class="flex flex-col items-center justify-center h-full gap-4 text-center"
      >
        <span class="text-5xl">📊</span>
        <p class="text-sm font-medium">Ask me anything about your ETFs</p>
        <p class="text-xs text-muted-foreground">
          Portfolio performance, ETF analysis, drawdown explanations.
        </p>
        <div class="flex flex-col gap-2 w-full max-w-sm mt-2">
          <button
            v-for="s in suggestions"
            :key="s"
            class="text-xs text-left px-3 py-2 rounded-lg border border-border
                   hover:bg-muted transition-colors text-muted-foreground"
            @click="input = s; send()"
          >
            {{ s }}
          </button>
        </div>
      </div>

      <MessageBubble
        v-for="msg in ai.messages"
        :key="msg.id"
        :message="msg"
      />

      <!-- Typing indicator -->
      <div v-if="ai.loading" class="flex items-start gap-2">
        <div class="bg-muted rounded-2xl rounded-bl-sm px-4 py-3 flex gap-1">
          <span
            v-for="n in 3"
            :key="n"
            class="w-1.5 h-1.5 rounded-full bg-muted-foreground animate-bounce"
            :style="{ animationDelay: `${(n - 1) * 150}ms` }"
          />
        </div>
      </div>
    </div>

    <!-- Input -->
    <div class="mt-4">
      <div class="flex gap-2 items-end">
        <textarea
          v-model="input"
          rows="2"
          placeholder="Ask about your portfolio or ETFs…"
          class="flex-1 resize-none bg-background border border-border rounded-xl
                 px-4 py-3 text-sm text-foreground placeholder:text-muted-foreground
                 focus:outline-none focus:ring-1 focus:ring-primary max-h-32"
          @keydown="onKeydown"
        />
        <button
          :disabled="!input.trim() || ai.loading"
          class="px-4 py-3 rounded-xl bg-primary text-primary-foreground text-sm
                 font-medium transition-opacity disabled:opacity-40 self-end"
          @click="send"
        >
          Send
        </button>
      </div>
      <p class="text-xs text-muted-foreground mt-1.5">Enter to send · Shift+Enter for new line</p>
    </div>
  </div>
</template>