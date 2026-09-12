#!/usr/bin/env node
// Development-time generation step (docs/arsiv/fazlar/84-TYPESCRIPT-ISTEMCISI-VE-NPM.md,
// section 84.2). `npm run build` and `dotnet build` do NOT call this — it is
// run by hand whenever docs/openapi/tracon.json changes, and its output
// (src/schema.ts) is committed.
//
// Flow: docs/openapi/tracon.json -> strip the '/tracon' prefix (a
// throwaway copy; the committed document is never touched) -> openapi-typescript.
//
// Exported as a function, not just a CLI script, so test/schema-drift.test.ts
// can call the exact same generation logic against a scratch path and diff it
// against the committed file — one definition of "how the schema is generated".

import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const packageRoot = resolve(here, '..');

// The installed CLI is invoked through this interpreter, NOT through 'npx': on
// Windows 'npx' is 'npx.cmd', which execFileSync cannot resolve (ENOENT) and
// cannot spawn without a shell — the Windows CI leg failed here while Linux
// passed. Resolving the bin entry also pins the generator to the version in
// package.json instead of whatever npx would fetch. The entry comes from the
// package's own 'bin' field rather than a resolved subpath: its exports map
// rewrites './*.js' to './*.mjs', so resolving 'bin/cli.js' asks for a file
// that does not exist (measured).
const openApiTypeScriptManifest = createRequire(import.meta.url).resolve(
  'openapi-typescript/package.json',
);
const openApiTypeScriptCli = resolve(
  dirname(openApiTypeScriptManifest),
  JSON.parse(readFileSync(openApiTypeScriptManifest, 'utf8')).bin['openapi-typescript'],
);

export const documentPath = resolve(packageRoot, '..', '..', 'docs', 'openapi', 'tracon.json');

/** Generates the schema module at `outputPath` from the committed OpenAPI document. */
export function generateSchema(outputPath) {
  const workDir = mkdtempSync(join(tmpdir(), 'tracon-client-'));
  const strippedPath = join(workDir, 'tracon.stripped.json');

  try {
    execFileSync(
      process.execPath,
      [join(here, 'strip-prefix.mjs'), documentPath, strippedPath],
      { stdio: 'inherit' },
    );

    execFileSync(
      process.execPath,
      [
        openApiTypeScriptCli,
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
