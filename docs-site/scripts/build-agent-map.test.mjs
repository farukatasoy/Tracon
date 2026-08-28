import assert from 'node:assert/strict';
import { test } from 'node:test';
import { win32 } from 'node:path';

import { packageName } from './build-agent-map.mjs';

test('package names are extracted from Windows paths', () => {
  assert.equal(
    packageName('D:\\a\\AgentPrism\\AgentPrism\\src\\AgentPrism.Core\\AgentPrism.Core.csproj', win32.basename),
    'AgentPrism.Core',
  );
});
