<script setup lang="ts">
import { RouterLink, useRoute } from 'vue-router'
import { computed } from 'vue'

const route = useRoute()

const navItems = [
  { to: '/',             label: 'Dashboard',    icon: '⬛' },
  { to: '/portfolios',   label: 'Portfolios',   icon: '📊' },
  { to: '/data-quality', label: 'Data Quality', icon: '🛡️' },
]

const isActive = (path: string) =>
  path === '/' ? route.path === '/' : route.path.startsWith(path)
</script>

<template>
  <aside class="w-60 h-full border-r border-border bg-card flex flex-col py-6 px-4 shrink-0">
    <!-- Logo -->
    <div class="mb-8 px-2">
      <h1 class="text-lg font-bold tracking-tight">ETF Insight</h1>
      <p class="text-xs text-muted-foreground mt-0.5">Platform</p>
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