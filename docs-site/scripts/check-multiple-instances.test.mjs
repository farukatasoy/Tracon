import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// The page "Run more than one instance" is the support statement for two instances. Every setting it names is one
// a reader will type into configuration, so every one of them must be written down in the configuration reference too:
// a support page that names a key the reference never lists sends the reader to a setting nobody documents.

const here = resolve(fileURLToPath(new URL('.', import.meta.url)));
const docs = join(here, '../src/content/docs');

/** `Tracon:RateLimit:Scope = Deployment` -> `RateLimit:Scope`; the section and the key, without the root. */
function settingNames(markdown) {
  const names = new Set();

  for (const [, span] of markdown.matchAll(/`([^`\n]+)`/g)) {
    const match = span.match(/^(?:Tracon:)?([A-Z][A-Za-z]+(?::[A-Z][A-Za-z]+)+)/);

    if (match) {
      names.add(match[1]);
    }
  }

  return [...names].sort();
}

test('every setting the support page names is listed in the configuration reference', () => {
  const guide = readFileSync(join(docs, 'guides/multiple-instances.md'), 'utf8');
  const reference = readFileSync(join(docs, 'reference/configuration.md'), 'utf8');
  const names = settingNames(guide);

  assert.ok(names.length >= 6, `the page names ${names.length} settings; the extractor has stopped finding them`);
  assert.deepEqual(
    names.filter((name) => !reference.includes(name)),
    [],
    'settings the page names and the configuration reference never lists',
  );
});

test('the extractor reads a section and a key with or without the root and ignores a value', () => {
  assert.deepEqual(
    settingNames('`Tracon:RateLimit:Scope = Deployment` and `Mcp:OAuthTokenStore` and `Scheduling.PollInterval` and `Foo`'),
    ['Mcp:OAuthTokenStore', 'RateLimit:Scope'],
  );
});
