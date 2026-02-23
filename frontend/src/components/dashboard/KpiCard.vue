<script setup lang="ts">
defineProps<{
  label: string
  value: string
  sub?: string
  trend?: 'up' | 'down' | 'neutral'
}>()

const trendClass = (trend?: string) => ({
  'text-emerald-500': trend === 'up',
  'text-red-500':     trend === 'down',
  'text-slate-400':   trend === 'neutral' || !trend,
})
</script>

<template>
  <div class="rounded-xl border border-border bg-card p-6 flex flex-col gap-2">
    <p class="text-xs text-muted-foreground uppercase tracking-widest font-medium">
      {{ label }}
    </p>
    <p class="text-3xl font-bold tracking-tight text-foreground">{{ value }}</p>
    <p v-if="sub" class="text-xs font-medium" :class="trendClass(trend)">
      {{ sub }}
    </p>
  </div>
</template>