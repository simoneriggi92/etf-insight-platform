import { ref, onMounted } from 'vue'
import { aiApi } from '../api/aiChat'

export function useAiSuggestions() {
  const suggestions = ref<string[]>([])

  onMounted(async () => {
    try {
      const { data } = await aiApi.getSuggestions()
      suggestions.value = data.suggestions
    } catch {
      suggestions.value = [
        'Quali ETF sono più adatti per investire in tecnologia USA?',
        'Dimmi gli ETF obbligazionari più sicuri',
        'Qual è la differenza tra SWDA e VWCE?',
      ]
    }
  })

  return { suggestions }
}
