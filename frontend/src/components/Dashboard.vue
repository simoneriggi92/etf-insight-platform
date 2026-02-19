<script setup lang="ts">
import ScanTriggerButton from '../components/ScanTriggerButton.vue'
</script>

<template>
  <main class="min-h-screen bg-gray-950 text-gray-100 p-8">
    <header class="mb-8">
      <h1 class="text-2xl font-bold tracking-tight">ETF Insight Platform</h1>
      <p class="text-gray-400 text-sm mt-1">Data quality monitoring dashboard</p>
    </header>

    <section class="mb-8">
      <h2 class="text-sm font-semibold text-gray-400 uppercase tracking-widest mb-3">
        Actions
      </h2>
      <ScanTriggerButton />
    </section>

    <!-- Anomalies table will go here next -->
  </main>
</template>