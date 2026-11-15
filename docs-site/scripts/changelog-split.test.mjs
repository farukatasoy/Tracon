import assert from 'node:assert/strict';
import { test } from 'node:test';

import { PART_LIMIT, countEntries, splitBody } from './changelog-split.mjs';

const entry = (name, size = 400) => `- **${name}.** ${'x'.repeat(size)}\n  What to do:\n  - **Nothing.**\n`;
const bytes = (text) => Buffer.byteLength(text);

test('a body that fits is one page and is returned as it is', () => {
  const body = `A short lead.\n\n${entry('a')}\n${entry('b')}`;

  assert.deepEqual(splitBody(body, 10_000), [body]);
});

test('the limit a page carries is the one the weight ceiling allows', () => {
  assert.equal(PART_LIMIT, 64_000);
});

test('a long body is cut between entries and keeps every entry once, in order', () => {
  const names = Array.from({ length: 30 }, (_, index) => `entry-${String(index).padStart(2, '0')}`);
  const body = `Lead paragraph.\n\n${names.map((name) => entry(name)).join('\n')}`;

  const parts = splitBody(body, 5_000);

  assert.ok(parts.length > 1, 'the body is longer than one page');

  const seen = parts.flatMap((part) => [...part.matchAll(/^- \*\*(entry-\d\d)\.\*\*/gm)].map((match) => match[1]));

  assert.deepEqual(seen, names);

  for (const part of parts) {
    assert.ok(bytes(part) <= 5_000, `a part of ${bytes(part)} bytes is over the limit`);
    // An entry is never cut: every "What to do" list stays with the entry that owns it.
    assert.equal(
      (part.match(/^- \*\*entry-/gm) ?? []).length,
      (part.match(/^  What to do:/gm) ?? []).length,
    );
  }
});

test('the pages are about the same size, so the last one is not a few lines', () => {
  const body = Array.from({ length: 40 }, (_, index) => entry(`e${index}`)).join('\n');

  const sizes = splitBody(body, 9_000).map(bytes);

  assert.ok(Math.max(...sizes) / Math.min(...sizes) < 1.4, `sizes ${sizes.join(', ')}`);
});

test('a part that begins inside a section names the section it continues', () => {
  const body = `## Fixed\n\n${Array.from({ length: 20 }, (_, index) => entry(`fix-${index}`)).join('\n')}`;

  const parts = splitBody(body, 4_000);

  assert.ok(parts.length > 1);
  assert.ok(parts[0].startsWith('## Fixed'));
  assert.ok(parts[0].includes('## Fixed') && !parts[0].includes('(continued)'));

  for (const part of parts.slice(1)) {
    assert.ok(part.startsWith('## Fixed (continued)'), part.slice(0, 40));
  }
});

test('a heading is never the last thing of a page: it goes with the entries that follow it', () => {
  const body = [
    entry('a', 1500),
    entry('b', 1500),
    '## Second section\n',
    entry('c', 1500),
    entry('d', 1500),
  ].join('\n');

  const parts = splitBody(body, 3_800);

  assert.ok(parts.length > 1);

  for (const part of parts) {
    const last = part.trim().split('\n').at(-1) ?? '';

    assert.ok(!last.startsWith('## '), `a part ends on a heading: ${last}`);
  }

  assert.ok(parts.some((part) => part.startsWith('## Second section')));
});

test('a list line or a heading inside a code fence is code, not a boundary', () => {
  const fenced = '- **Entry.** The command:\n\n  ```bash\n  - not an entry\n  ## not a heading\n  ```\n';
  const body = `${fenced}\n${entry('other', 3000)}\n${entry('third', 3000)}`;

  assert.equal(countEntries(body), 3);

  for (const part of splitBody(body, 4_000)) {
    // The fenced block is whole in whichever part holds it.
    assert.equal((part.match(/```/g) ?? []).length % 2, 0);
  }
});

test('countEntries counts the top-level entries and nothing under them', () => {
  assert.equal(countEntries(`Lead.\n\n${entry('a')}\n${entry('b')}\n## S\n\n${entry('c')}`), 3);
  assert.equal(countEntries('Only a lead.'), 0);
});
