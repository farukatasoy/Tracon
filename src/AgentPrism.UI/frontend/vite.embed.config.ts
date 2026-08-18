import { defineConfig } from 'vite';

/**
 * Second, separate build for the embeddable chat widget (Phase 61).
 *
 * Deliberately its own config, not a second entry on the console's
 * `vite.config.ts`: the console and the widget have different budgets
 * (250 KB vs 30 KB gzip — `scripts/postbuild.mjs`), and mixing entries in
 * one Vite build would let console code leak into the widget's bundle
 * without either budget catching it.
 *
 * Library mode with the `iife` format produces ONE self-executing,
 * non-hashed file (`embed.js`) with no code splitting — the embedding page
 * adds exactly one `<script src="…/embed/embed.js">` tag, and the build
 * output name must stay stable across releases since third-party pages
 * hardcode the URL.
 */
export default defineConfig({
  build: {
    outDir: '../wwwroot/embed',
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: false,
    reportCompressedSize: false,
    cssCodeSplit: false,
    lib: {
      entry: 'src/embed/main.ts',
      formats: ['iife'],
      name: 'AgentPrismEmbedBundle',
      fileName: () => 'embed.js',
    },
  },
});
