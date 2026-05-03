import path from 'path';
import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

// VITE_API_BASE_URL is required for production builds.
// In development it may be left empty — Vite will proxy /api/* to http://localhost:5000.
const REQUIRED_PROD_ENV_VARS = ['VITE_API_BASE_URL'];

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');

  // Only enforce non-empty value in production builds
  if (mode === 'production') {
    const missing = REQUIRED_PROD_ENV_VARS.filter((key) => !env[key]);
    if (missing.length > 0) {
      throw new Error(
        `[UPACIP] Missing required environment variables for production build:\n${missing.map((k) => `  - ${k}`).join('\n')}\n` +
          'Set VITE_API_BASE_URL to the backend base URL (e.g. https://api.yourdomain.com).',
      );
    }
  }

  // When VITE_API_BASE_URL is empty in dev, proxy /api/* and /health to the local backend
  const apiBaseUrl = env['VITE_API_BASE_URL'] || 'http://localhost:5000';
  const useProxy = !env['VITE_API_BASE_URL'];

  return {
    base: '/',
    plugins: [react()],
    resolve: {
      alias: {
        '@': path.resolve(__dirname, 'src'),
      },
    },
    build: {
      outDir: 'dist',
      // Content hashing is enabled by default in Vite; explicit for clarity
      rollupOptions: {
        output: {
          assetFileNames: 'assets/[name]-[hash][extname]',
          chunkFileNames: 'assets/[name]-[hash].js',
          entryFileNames: 'assets/[name]-[hash].js',
        },
      },
    },
    server: {
      port: 3000,
      ...(useProxy && {
        proxy: {
          '/api': {
            target: apiBaseUrl,
            changeOrigin: true,
            secure: false,
          },
          '/health': {
            target: apiBaseUrl,
            changeOrigin: true,
            secure: false,
          },
        },
      }),
    },
  };
});
