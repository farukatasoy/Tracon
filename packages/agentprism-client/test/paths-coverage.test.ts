import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { documentPath } from '../scripts/generate.mjs';
import { stripPrefix } from '../scripts/strip-prefix.mjs';

const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');

/**
 * `paths` is a compile-time-only interface — there is no runtime object to
 * introspect, so coverage is read the same way a human would review the
 * generated file: the quoted key that opens each top-level path entry.
 */
function extractGeneratedPaths(schemaSource: string): string[] {
  const pathsBlock = schemaSource.match(/export interface paths \{([\s\S]*?)\n\}/u);

  if (!pathsBlock) {
    throw new Error('Could not find the `paths` interface in src/schema.ts.');
  }

  return [...pathsBlock[1].matchAll(/^ {4}"([^"]+)":\s*\{/gmu)].map((match) => match[1]);
}

describe('paths coverage (section 84.10)', () => {
  it('the generated paths type has an entry for every path in the OpenAPI document', () => {
    const document = stripPrefix(JSON.parse(readFileSync(documentPath, 'utf8')));
    const expected = Object.keys(document.paths).sort();

    const schemaSource = readFileSync(resolve(packageRoot, 'src', 'schema.ts'), 'utf8');
    const actual = extractGeneratedPaths(schemaSource).sort();

    expect(actual).toEqual(expected);
  });
});
