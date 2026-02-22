<script setup lang="ts">
import AppSidebar from '../components/AppSidebar.vue'
import AiAdvisorPanel  from '../components/ai-advisor/AiAdvisorPanel.vue'
import { useAiStore }  from '../stores/aiStore'

const ai = useAiStore()
</script>   


<template>
  <div class="flex h-screen bg-background text-foreground overflow-hidden">
    <AppSidebar />

    <main class="flex-1 overflow-y-auto p-8">
      <RouterView />
    </main>

    <!-- AI Advisor — floating, available on every route -->
    <AiAdvisorPanel />

    <!-- Toggle button -->
    <button
      class="fixed bottom-4 right-4 z-50 w-12 h-12 rounded-full bg-primary
             text-primary-foreground shadow-lg flex items-center justify-center
             text-xl hover:scale-105 transition-transform"
      :title="ai.isOpen ? 'Close AI Advisor' : 'Open AI Advisor'"
      @click="ai.toggle()"
    >
      {{ ai.isOpen ? '✕' : '🤖' }}
    </button>
  </div>
</template>