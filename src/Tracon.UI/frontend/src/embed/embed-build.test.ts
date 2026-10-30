import { describe, expect, it } from 'vitest';
import { build } from 'vite';
import embedConfig from '../../vite.embed.config';

/**
 * Builds the widget in memory with the SHIPPED `vite.embed.config.ts` and
 * inspects the emitted file. The config is the only place output options live,
 * so a bundler major that changes a default shows up here, not on a customer's
 * page (Rolldown dropped the IIFE's `"use strict"` that Rollup
 * emitted, and nothing else noticed — 418 tests and the E2E suite stayed green).
 *
 * The third-party-notices plugin is dropped: it records modules to disk, and
 * this test must leave no trace. It does not influence the output text.
 */
async function buildWidget(): Promise<{ fileNames: string[]; code: string }> {
  const result = await build({
    ...embedConfig,
    configFile: false,
    logLevel: 'silent',
    plugins: [],
    build: { ...embedConfig.build, write: false, emptyOutDir: false },
  });

  const outputs = (Array.isArray(result) ? result : [result]).flatMap((entry) =>
    'output' in entry ? entry.output : [],
  );
  const chunk = outputs.find((output) => output.type === 'chunk');

  return { fileNames: outputs.map((output) => output.fileName), code: chunk?.code ?? '' };
}

describe('embeddable widget build output', () => {
  it('is one file named embed.js, because third-party pages hardcode the URL', async () => {
    const { fileNames } = await buildWidget();

    expect(fileNames).toEqual(['embed.js']);
  });

  it('runs in strict mode on the embedding page', async () => {
    const { code } = await buildWidget();

    // Rollup put the directive inside the IIFE, Rolldown puts it in front of it; both make the
    // whole widget strict. A directive is a plain string literal, a template literal is not one.
    expect(code.slice(0, 40)).toMatch(/^(?:\(function\(\)\{\s*)?(["'])use strict\1/);
  });
});
