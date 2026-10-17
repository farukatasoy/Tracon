import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    environment: 'node',
    include: ['test/**/*.test.ts'],

    // `*.test-d.ts` files hold type-level assertions (`expectTypeOf`). Without this they
    // never run: tsc's own config only includes `src`, and a runtime test cannot see types.
    typecheck: {
      enabled: true,
      include: ['test/**/*.test-d.ts'],
      tsconfig: './tsconfig.test.json',
    },
  },
});
