import assert from 'node:assert/strict';
import { test } from 'node:test';
import { win32 } from 'node:path';

import { packageName, splitRow } from './build-agent-map.mjs';

test('package names are extracted from Windows paths', () => {
  assert.equal(
    packageName('D:\\a\\Tracon\\Tracon\\src\\Tracon.Core\\Tracon.Core.csproj', win32.basename),
    'Tracon.Core',
  );
});

test('an escaped pipe stays inside its cell', () => {
  // `<runId\|previous>` is the only way to write an alternation in a Markdown
  // table. Splitting on every pipe truncated the cell at the escape and shipped
  // a dangling backslash into the agent map.
  assert.deepEqual(
    splitRow('| Relative CI gate | `--baseline <runId\\|previous>` | Fails on cases that broke |'),
    ['Relative CI gate', '`--baseline <runId|previous>`', 'Fails on cases that broke'],
  );
});

test('the separator row is still recognised', () => {
  assert.equal(splitRow('|---|---|---|'), null);
});
