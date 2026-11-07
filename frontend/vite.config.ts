import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Geliştirmede /api istekleri .NET backend'e proxy'lenir.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5000',
        changeOrigin: true,
      },
    },
  },
})
