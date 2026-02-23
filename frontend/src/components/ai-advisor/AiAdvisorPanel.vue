<!-- filepath: /Users/simone/Documents/PersonalProjects/etf-insight-platform/frontend/src/components/ai-advisor/AiAdvisorPanel.vue -->
<script setup lang="ts">
import { ref, nextTick, watch } from 'vue'
import { useAiStore } from '../../stores/aiStore'
import { usePortfoliosStore } from '../../stores/portfolios'
import MessageBubble from './MessageBubble.vue'

const ai        = useAiStore()
const portfolio = usePortfoliosStore()

const input     = ref('')
const scrollEl  = ref<HTMLElement>()

async function send() {
  const q = input.value.trim()
  if (!q || ai.loading) return
  input.value = ''
  await ai.send({
    question:    q
    // portfolioId: portfolio.activeId ?? undefined,
  })
  await nextTick()
  scrollEl.value?.scrollTo({ top: scrollEl.value.scrollHeight, behavior: 'smooth' })
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send() }
}

// Auto-scroll on new messages
watch(() => ai.messages.length, async () => {
  await nextTick()
  scrollEl.value?.scrollTo({ top: scrollEl.value.scrollHeight, behavior: 'smooth' })
})
</script>

<template>
  <Transition name="slide">
    <div
      v-if="ai.isOpen"
      class="fixed z-50 flex flex-col rounded-2xl border border-border bg-card shadow-2xl overflow-hidden
             inset-4
             sm:inset-auto sm:bottom-20 sm:right-4 sm:w-96 sm:h-[560px]"
    >
      <!-- Header -->
      <div class="flex items-center justify-between px-4 py-3 border-b border-border bg-card">
        <div class="flex items-center gap-2">
          <span class="text-lg">🤖</span>
          <div>
            <p class="text-sm font-semibold text-foreground">AI Advisor</p>
            <p class="text-xs text-muted-foreground">
              {{ portfolio.activePortfolio?.name ?? 'ETF Knowledge Base' }}
            </p>
          </div>
        </div>
        <div class="flex items-center gap-2">
          <button
            class="text-xs text-muted-foreground hover:text-foreground transition-colors"
            @click="ai.clear()"
          >
            Clear
          </button>
          <button
            class="text-muted-foreground hover:text-foreground transition-colors"
            @click="ai.toggle()"
          >
            ✕
          </button>
        </div>
      </div>

      <!-- Messages -->
      <div ref="scrollEl" class="flex-1 overflow-y-auto p-4 flex flex-col gap-4">
        <!-- Welcome -->
        <div v-if="ai.messages.length === 0"
          class="flex flex-col items-center justify-center h-full gap-3 text-center">
          <span class="text-4xl">📊</span>
          <p class="text-sm font-medium text-foreground">Ask me anything</p>
          <p class="text-xs text-muted-foreground leading-relaxed">
            Portfolio performance, ETF analysis,<br>drawdown explanations, allocation advice.
          </p>
          <!-- Suggested prompts -->
          <div class="flex flex-col gap-2 w-full mt-2">
            <button
              v-for="prompt in [
                'What is my current portfolio performance?',
                'Which ETFs have the highest volatility?',
                'Explain my max drawdown',
              ]"
              :key="prompt"
              class="text-xs text-left px-3 py-2 rounded-lg border border-border
                     hover:bg-muted transition-colors text-muted-foreground"
              @click="input = prompt; send()"
            >
              {{ prompt }}
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
            <span v-for="n in 3" :key="n"
              class="w-1.5 h-1.5 rounded-full bg-muted-foreground animate-bounce"
              :style="{ animationDelay: `${(n - 1) * 150}ms` }"
            />
          </div>
        </div>
      </div>

      <!-- Input -->
      <div class="p-3 border-t border-border bg-card">
        <div class="flex gap-2 items-end">
          <textarea
            v-model="input"
            rows="1"
            placeholder="Ask about your portfolio…"
            class="flex-1 resize-none bg-background border border-border rounded-xl
                   px-3 py-2 text-sm text-foreground placeholder:text-muted-foreground
                   focus:outline-none focus:ring-1 focus:ring-primary max-h-24"
            @keydown="onKeydown"
          />
          <button
            :disabled="!input.trim() || ai.loading"
            class="px-3 py-2 rounded-xl bg-primary text-primary-foreground text-sm
                   font-medium transition-opacity disabled:opacity-40"
            @click="send"
          >
            ↑
          </button>
        </div>
        <p class="text-xs text-muted-foreground mt-1.5 px-1">
          Enter to send · Shift+Enter for new line
        </p>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.slide-enter-active,
.slide-leave-active { transition: all 0.25s ease; }
.slide-enter-from,
.slide-leave-to     { opacity: 0; transform: translateY(12px) scale(0.97); }
</style>