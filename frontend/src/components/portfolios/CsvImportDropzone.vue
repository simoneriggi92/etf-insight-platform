<script setup lang="ts">
import { ref } from 'vue'
import Papa from 'papaparse'

const emit = defineEmits<{ (e: 'file-selected', file: File, preview: any[]): void }>()

const dragging  = ref(false)
const preview   = ref<any[]>([])
const fileName  = ref<string | null>(null)

function handleFile(file: File) {
  fileName.value = file.name
  Papa.parse(file, {
    header: true,
    skipEmptyLines: true,
    complete(results) {
      preview.value = results.data.slice(0, 5)   // show first 5 rows
      emit('file-selected', file, results.data)
    },
  })
}

function onDrop(e: DragEvent) {
  dragging.value = false
  const file = e.dataTransfer?.files[0]
  if (file) handleFile(file)
}

function onInput(e: Event) {
  const file = (e.target as HTMLInputElement).files?.[0]
  if (file) handleFile(file)
}
</script>

<template>
  <div>
    <!-- Drop zone -->
    <label
      class="flex flex-col items-center justify-center gap-2 rounded-lg border-2 border-dashed
             border-border bg-muted/40 py-10 cursor-pointer transition-colors"
      :class="dragging ? 'border-primary bg-primary/5' : 'hover:border-primary/50'"
      @dragover.prevent="dragging = true"
      @dragleave="dragging = false"
      @drop.prevent="onDrop"
    >
      <span class="text-3xl">📂</span>
      <span class="text-sm font-medium">Drop a CSV file here or click to browse</span>
      <span class="text-xs text-muted-foreground">
        Columns: ticker · transaction_date · type · units · price_per_unit · fees
      </span>
      <span v-if="fileName" class="text-xs text-primary font-medium mt-1">{{ fileName }}</span>
      <input type="file" accept=".csv,text/csv" class="hidden" @change="onInput" />
    </label>

    <!-- Preview table -->
    <div v-if="preview.length" class="mt-4 overflow-x-auto rounded-md border border-border">
      <p class="text-xs text-muted-foreground px-3 py-2 bg-muted/30 border-b border-border">
        Preview (first {{ preview.length }} rows)
      </p>
      <table class="w-full text-xs">
        <thead>
          <tr class="text-muted-foreground border-b border-border">
            <th v-for="col in Object.keys(preview[0])" :key="col"
              class="text-left px-3 py-1.5 font-medium">{{ col }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(row, i) in preview" :key="i" class="border-b border-border last:border-0">
            <td v-for="col in Object.keys(row)" :key="col" class="px-3 py-1.5">{{ row[col] }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>