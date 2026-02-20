<script setup lang="ts">
import AppSidebar from '../components/AppSidebar.vue'
</script>   


<template>
  <div class="flex h-screen bg-background text-foreground overflow-hidden">
    <AppSidebar />
    <main class="flex-1 overflow-y-auto p-8">
      <RouterView />
    </main>
  </div>
</template>