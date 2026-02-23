<script setup lang="ts">
import { RouterLink, useRoute } from 'vue-router'

const props = defineProps<{ open?: boolean }>()
const emit  = defineEmits<{ (e: 'close'): void }>()

const route = useRoute()

const navItems = [
  { to: '/',             label: 'Dashboard',    icon: '⬛' },
  { to: '/portfolios',   label: 'Portfolios',   icon: '📊' },
  { to: '/data-quality', label: 'Data Quality', icon: '🛡️' },
]

const isActive = (path: string) =>
  path === '/' ? route.path === '/' : route.path.startsWith(path)

function handleNavClick() {
  emit('close')   // close overlay on mobile after navigation
}
</script>

<template>
  <!--
    Desktop (md+): static sidebar always visible
    Mobile (<md):  fixed overlay, slides in when open=true
  -->
  <aside
    :class="[
      // base styles
      'h-full border-r border-border bg-card flex flex-col py-6 px-4 shrink-0 z-40',
      // desktop: always in flow, fixed width
      'md:static md:translate-x-0 md:w-60',
      // mobile: fixed overlay
      'fixed inset-y-0 left-0 w-72',
      // mobile visibility controlled by open prop
      props.open ? 'translate-x-0' : '-translate-x-full md:translate-x-0',
      'transition-transform duration-200 ease-in-out',
    ]"
  >
      <!-- Logo + close button row -->
      <div class="mb-8 px-2 flex items-start justify-between">
        <div>
          <h1 class="text-lg font-bold tracking-tight">ETF Insight</h1>
          <p class="text-xs text-muted-foreground mt-0.5">Platform</p>
        </div>
        <!-- Close button — mobile only -->
        <button
          class="md:hidden p-1.5 rounded-md hover:bg-accent transition-colors"
          aria-label="Close menu"
          @click="emit('close')"
        >
          <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"/>
          </svg>
        </button>
      </div>

      <!-- Nav -->
      <nav class="flex flex-col gap-1">
        <RouterLink
          v-for="item in navItems"
          :key="item.to"
          :to="item.to"
          class="flex items-center gap-3 px-3 py-2 rounded-md text-sm font-medium transition-colors"
          :class="isActive(item.to)
            ? 'bg-primary text-primary-foreground'
            : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'"
          @click="handleNavClick"
        >
          <span>{{ item.icon }}</span>
          <span>{{ item.label }}</span>
        </RouterLink>
      </nav>

      <!-- Footer -->
      <div class="mt-auto px-2">
        <p class="text-xs text-muted-foreground">Week 8 · Event-driven</p>
      </div>
    </aside>
</template>