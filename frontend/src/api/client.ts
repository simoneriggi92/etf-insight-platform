import axios from 'axios'

export const apiClient = axios.create({
  baseURL: '/api',          // proxied by Vite → http://localhost:5001/api
  headers: { 'Content-Type': 'application/json' },
  withCredentials: true,    // mirrors AllowCredentials() on backend
})