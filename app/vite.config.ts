import path from 'path';
import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

// Required environment variables — build fails with a descriptive error if absent
const REQUIRED_ENV_VARS = ['VITE_API_BASE_URL'];

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');

  const missing = REQUIRED_ENV_VARS.filter((key) => !env[key]);
  if (missing.length > 0) {
    throw new Error(
      `[UPACIP] Missing required environment variables:\n${missing.map((k) => `  - ${k}`).join('\n')}\n` +
        'Copy .env.example to .env and fill in the required values.',
    );
  }

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
    },
  };
});
