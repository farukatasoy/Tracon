import { defineConfig } from 'vitest/config';

/**
 * Unit tests cover pure logic only: formatting, SSE framing and run-event
 * folding. Rendering behaviour is verified end to end with Playwright against a
 * real host, where it is worth the cost.
 */
export default defineConfig({
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
});
