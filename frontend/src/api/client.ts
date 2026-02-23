import axios from 'axios'

export const apiClient = axios.create({
  baseURL: '/api',           // relative → nginx proxy handles routing
  headers: { 'Content-Type': 'application/json' },
  withCredentials: true,    // mirrors AllowCredentials() on backend
})