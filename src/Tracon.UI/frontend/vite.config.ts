import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

/**
 * Tracon can be mapped under any prefix (`/tracon`, `/panel`, ...). The
 * prefix is only known at run time, so every asset URL must be relative and
 * resolved through the `<base href>` tag that the .NET host injects.
 *
 * In development the app is served from the Vite root and talks to a locally
 * running host through the proxy below.
 */
export default defineConfig({
  base: './',
  plugins: [react(), tailwindcss()],
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: false,
    // Source maps would more than double the embedded payload and are not
    // useful without the original sources, which are not shipped.
    reportCompressedSize: false,
  },
  server: {
    port: 5173,
    proxy: {
      '/tracon': {
        target: 'http://localhost:5080',
        changeOrigin: false,
      },
    },
  },
});
