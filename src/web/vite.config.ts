/// <reference types="vitest" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

/** Backend origin during `npm run dev`; the API is same-origin in production. */
const API_ORIGIN = process.env.WEGO_API_ORIGIN ?? 'http://localhost:5080'

/**
 * The API lives at the site root (`/trips`, `/session`) rather than under an
 * `/api` prefix, so the dev server proxies exactly those paths and lets Vite
 * serve everything else.
 */
const API_PATHS = ['/trips', '/session', '/hubs']

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: Object.fromEntries(
      API_PATHS.map((path) => [
        path,
        // ws so the SignalR hub can upgrade through the dev server.
        { target: API_ORIGIN, changeOrigin: false, ws: path === '/hubs' },
      ]),
    ),
  },
  build: {
    // The backend serves the built SPA from wwwroot as a single deployable.
    outDir: '../WeGo.Api/wwwroot',
    emptyOutDir: true,
    sourcemap: true,
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./vitest.setup.ts'],
    css: false,
  },
})
