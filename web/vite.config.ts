/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

/** Dépendances stables regroupées pour tirer parti du cache navigateur entre deux déploiements. */
const VENDOR_CHUNKS: Record<string, string[]> = {
  react: ['react', 'react-dom', 'react-router'],
  query: ['@tanstack'],
  dnd: ['@dnd-kit'],
};

/**
 * Configuration Vite.
 *
 * En développement, les appels `/api` sont relayés vers le backend : le navigateur
 * ne voit qu'une seule origine, ce qui évite d'avoir à configurer CORS localement.
 */
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:5080',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: false,
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (!id.includes('node_modules')) {
            return undefined;
          }

          for (const [chunk, packages] of Object.entries(VENDOR_CHUNKS)) {
            if (packages.some((name) => id.includes(`node_modules/${name}`))) {
              return chunk;
            }
          }

          return undefined;
        },
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
  },
});
