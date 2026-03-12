import axios from 'axios'
import { useGuestSession } from '@/composables/useGuestSession'

const { guestId } = useGuestSession()


export const apiClient = axios.create({
  baseURL: '/api',           // relative → nginx proxy handles routing
  headers: { 'Content-Type': 'application/json' },
  withCredentials: true,    // mirrors AllowCredentials() on backend
})

// Attach guest ID to all outgoing requests, ensuring consistent session tracking without user accounts
apiClient.interceptors.request.use(config => {
  config.headers['X-Guest-Id'] = guestId.value
  return config
})

// If the server auto-generated a new id, persist it for the next requests
apiClient.interceptors.response.use(response => {
  const serverGuestId = response.headers['x-guest-id']
  if (serverGuestId && serverGuestId !== guestId.value) {
    localStorage.setItem('etf_guest_id', serverGuestId)
    guestId.value = serverGuestId
  }
  return response
})