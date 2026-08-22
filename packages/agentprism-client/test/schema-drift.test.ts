import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { generateSchema } from '../scripts/generate.mjs';

const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');

describe('schema drift (section 84.10)', () => {
  it('generating from the committed OpenAPI document reproduces the committed schema module', () => {
    const workDir = mkdtempSync(join(tmpdir(), 'agentprism-client-drift-'));
    const generatedPath = join(workDir, 'schema.ts');

    try {
      generateSchema(generatedPath);

      const generated = readFileSync(generatedPath, 'utf8');
      const committed = readFileSync(join(packageRoot, 'src', 'schema.ts'), 'utf8');

      expect(generated).toBe(committed);
    } finally {
      rmSync(workDir, { recursive: true, force: true });
    }
  });
});
