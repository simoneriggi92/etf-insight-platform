<script setup lang="ts">
import { ref } from 'vue'
import AppSidebar from '../components/AppSidebar.vue'
import AiAdvisorPanel from '../components/ai-advisor/AiAdvisorPanel.vue'
import { useAiStore } from '../stores/aiStore'

const ai = useAiStore()
const sidebarOpen = ref(false)
</script>

<template>
  <div class="flex h-screen bg-background text-foreground overflow-hidden">

    <!-- Mobile overlay backdrop -->
    <Transition name="fade">
      <div
        v-if="sidebarOpen"
        class="fixed inset-0 z-30 bg-black/50 md:hidden"
        @click="sidebarOpen = false"
      />
    </Transition>

    <!-- Sidebar -->
    <AppSidebar
      :open="sidebarOpen"
      @close="sidebarOpen = false"
    />

    <!-- Main area -->
    <div class="flex-1 flex flex-col overflow-hidden">

      <!-- Mobile top bar -->
      <header class="md:hidden flex items-center gap-3 px-4 py-3 border-b border-border bg-card shrink-0">
        <button
          class="p-1.5 rounded-md hover:bg-accent transition-colors"
          @click="sidebarOpen = true"
          aria-label="Open menu"
        >
          <svg class="w-5 h-5 text-foreground" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 6h16M4 12h16M4 18h16" />
          </svg>
        </button>
        <span class="text-sm font-semibold">ETF Insight</span>
      </header>

      <main class="flex-1 overflow-y-auto p-4 sm:p-6 lg:p-8">
        <RouterView />
      </main>
    </div>

    <!-- AI Advisor floating panel -->
    <AiAdvisorPanel />

    <!-- AI toggle button -->
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

<style scoped>
.fade-enter-active, .fade-leave-active { transition: opacity 0.2s ease; }
.fade-enter-from, .fade-leave-to       { opacity: 0; }
</style>