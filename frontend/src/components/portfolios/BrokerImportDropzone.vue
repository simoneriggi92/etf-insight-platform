<script setup lang="ts">
import { computed, ref } from 'vue'

const emit = defineEmits<{
  (e: 'files-selected', files: File[]): void
}>()

const dragging = ref(false)
const selectedFiles = ref<File[]>([])
const rejectedFiles = ref<string[]>([])

const visibleFiles = computed(() => selectedFiles.value.slice(0, 8))

function updateSelection(input: FileList | File[]) {
  const files = Array.from(input)

  selectedFiles.value = files.filter(file =>
    file.name.toLowerCase().endsWith('.pdf'),
  )

  rejectedFiles.value = files
    .filter(file => !file.name.toLowerCase().endsWith('.pdf'))
    .map(file => file.name)

  emit('files-selected', selectedFiles.value)
}

function onDrop(event: DragEvent) {
  dragging.value = false

  if (event.dataTransfer?.files?.length) {
    updateSelection(event.dataTransfer.files)
  }
}

function onInput(event: Event) {
  const files = (event.target as HTMLInputElement).files

  if (files?.length) {
    updateSelection(files)
  }
}
</script>

<template>
  <div class="space-y-4">
    <label
      class="flex cursor-pointer flex-col items-center justify-center gap-3 rounded-xl border-2 border-dashed border-border bg-muted/30 px-6 py-12 text-center transition-colors"
      :class="dragging ? 'border-primary bg-primary/5' : 'hover:border-primary/50'"
      @dragover.prevent="dragging = true"
      @dragleave="dragging = false"
      @drop.prevent="onDrop"
    >
      <span class="text-4xl">📄</span>
      <div class="space-y-1">
        <p class="text-base font-semibold">Drop Trade Republic PDFs here or click to browse</p>
        <p class="text-sm text-muted-foreground">
          Upload up to 100 PDF files per batch. Each file must be smaller than 10 MB.
        </p>
      </div>
      <p v-if="selectedFiles.length" class="text-sm font-medium text-primary">
        {{ selectedFiles.length }} PDF file(s) selected
      </p>
      <input
        type="file"
        multiple
        accept=".pdf,application/pdf"
        class="hidden"
        @change="onInput"
      />
    </label>

    <div
      v-if="visibleFiles.length"
      class="rounded-xl border border-border bg-card p-4"
    >
      <p class="text-xs font-semibold uppercase tracking-[0.2em] text-muted-foreground">
        Selected files
      </p>

      <ul class="mt-3 divide-y divide-border">
        <li
          v-for="file in visibleFiles"
          :key="file.name"
          class="py-2 text-sm text-foreground"
        >
          {{ file.name }}
        </li>
      </ul>

      <p
        v-if="selectedFiles.length > visibleFiles.length"
        class="mt-3 text-xs text-muted-foreground"
      >
        + {{ selectedFiles.length - visibleFiles.length }} more file(s)
      </p>
    </div>

    <p
      v-if="rejectedFiles.length"
      class="text-sm text-amber-600"
    >
      Skipped non-PDF file(s): {{ rejectedFiles.join(', ') }}
    </p>
  </div>
</template>