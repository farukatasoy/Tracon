#!/usr/bin/env node
// Development-time generation step (docs/84-TYPESCRIPT-ISTEMCISI-VE-NPM.md,
// section 84.2). `npm run build` and `dotnet build` do NOT call this — it is
// run by hand whenever docs/openapi/agentprism.json changes, and its output
// (src/schema.ts) is committed.
//
// Flow: docs/openapi/agentprism.json -> strip the '/agentprism' prefix (a
// throwaway copy; the committed document is never touched) -> openapi-typescript.
//
// Exported as a function, not just a CLI script, so test/schema-drift.test.ts
// can call the exact same generation logic against a scratch path and diff it
// against the committed file — one definition of "how the schema is generated".

import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const packageRoot = resolve(here, '..');

export const documentPath = resolve(packageRoot, '..', '..', 'docs', 'openapi', 'agentprism.json');

/** Generates the schema module at `outputPath` from the committed OpenAPI document. */
export function generateSchema(outputPath) {
  const workDir = mkdtempSync(join(tmpdir(), 'agentprism-client-'));
  const strippedPath = join(workDir, 'agentprism.stripped.json');

  try {
    execFileSync('node', [join(here, 'strip-prefix.mjs'), documentPath, strippedPath], {
      stdio: 'inherit',
    });

    execFileSync(
      'npx',
      [
        'openapi-typescript',
        strippedPath,
        '-o',
        outputPath,
        // Section 84.6/Open Question 1: each schema also gets a readable root-level
        // type alias (e.g. `export type RunRecord = ...`), matching what the 155
        // migrated call sites already spell today.
        '--root-types',
        '--root-types-no-schema-prefix',
      ],
      { stdio: 'inherit' },
    );
  } finally {
    rmSync(workDir, { recursive: true, force: true });
  }
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  // A plain '.ts' file, not '.d.ts': the content is all type declarations
  // (zero runtime), but 'tsc' only copies a source file into dist/ as part of
  // its own compile — an INPUT '.d.ts' is treated as already-emitted and is
  // never written to outDir. Without a dist/schema.d.ts, index.js's
  // `export * from './schema.js'` would not resolve for a consumer.
  generateSchema(join(packageRoot, 'src', 'schema.ts'));
}
