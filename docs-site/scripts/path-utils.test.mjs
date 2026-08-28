import assert from 'node:assert/strict';
import test from 'node:test';
import { toPosixPath } from './path-utils.mjs';

test('normalizes Windows paths for portable content identifiers', () => {
  assert.equal(toPosixPath('guides\\background-work.md'), 'guides/background-work.md');
});
