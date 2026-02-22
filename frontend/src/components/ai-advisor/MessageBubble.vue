<!-- filepath: /Users/simone/Documents/PersonalProjects/etf-insight-platform/frontend/src/components/ai-advisor/MessageBubble.vue -->
<script setup lang="ts">
import type { AiMessage } from '../../types'
defineProps<{ message: AiMessage }>()
</script>

<template>
  <div class="flex flex-col gap-1"
    :class="message.role === 'user' ? 'items-end' : 'items-start'">

    <!-- Bubble -->
    <div class="max-w-[85%] rounded-2xl px-4 py-2.5 text-sm leading-relaxed"
      :class="message.role === 'user'
        ? 'bg-primary text-primary-foreground rounded-br-sm'
        : 'bg-muted text-foreground rounded-bl-sm'">
      {{ message.content }}
    </div>

    <!-- Sources (assistant only) -->
    <div v-if="message.sources?.length" class="flex flex-wrap gap-1.5 mt-1 max-w-[85%]">
      <span
        v-for="src in message.sources" :key="src.ticker"
        class="inline-flex items-center gap-1 px-2 py-0.5 rounded-full
               bg-indigo-500/10 text-indigo-400 text-xs font-medium border border-indigo-500/20"
        :title="src.excerpt"
      >
        📄 {{ src.ticker }}
        <span class="text-indigo-500/60">{{ (src.similarity * 100).toFixed(0) }}%</span>
      </span>
    </div>

    <!-- Timestamp -->
    <span class="text-xs text-muted-foreground px-1">
      {{ new Date(message.timestamp).toLocaleTimeString('en-GB', { timeStyle: 'short' }) }}
    </span>
  </div>
</template>