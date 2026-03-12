import { ref } from 'vue'

const STORAGE_KEY = 'etf_guest_id'

function resolveGuestId(): string {
  const stored = localStorage.getItem(STORAGE_KEY)
  if (stored) return stored

  // crypto.randomUUID() is available in all modern browsers and over HTTPS
  const id = crypto.randomUUID()
  localStorage.setItem(STORAGE_KEY, id)
  return id
}

// Module-level singleton — same id is reused across composable calls
const guestId = ref<string>(resolveGuestId())

export function useGuestSession() {
  return { guestId }
}