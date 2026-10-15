import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

/**
 * jsdom, so both kinds of test this suite carries can share one run: pure
 * logic (formatting, SSE framing, run-event folding — most of the `.test.ts`
 * files) and component tests (`.test.tsx`) that render a screen with
 * `@testing-library/react`. jsdom is a superset of what the pure tests need,
 * so this does not change what they exercise — cross-boundary rendering
 * behaviour (real network, real browser APIs) still belongs to Playwright.
 */
export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.ts', 'src/**/*.test.tsx'],
    // Above the 5 s a single findBy*/waitFor may now wait (src/test/setup.ts), so a slow
    // wait fails as "unable to find", naming the element, not as a bare test timeout.
    testTimeout: 20_000,
  },
});
