import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { checkConsoleScreens } from './check-console-screens.mjs';

function fixture(t, entries = '"nav.agents": \'Agents\',') {
  const root = mkdtempSync(join(tmpdir(), 'tracon-screenshot-gate-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const source = join(root, 'src');
  const docs = join(root, 'docs');
  const site = join(root, 'site');
  const locales = join(source, 'Tracon.UI/frontend/src/locales');
  mkdirSync(join(locales, 'en'), { recursive: true });
  mkdirSync(docs, { recursive: true });
  mkdirSync(join(site, 'public/screenshots'), { recursive: true });
  writeFileSync(join(locales, 'en.ts'), "export { enCommon } from './en/common';\n");
  writeFileSync(join(locales, 'en/common.ts'), `export const enCommon = { ${entries} };`);
  writeFileSync(join(docs, 'ui.md'), '## Agents\n\nThe agent catalog.\n');
  return { source, docs, site };
}

test('an empty navigation inventory fails instead of checking zero screenshots', (t) => {
  const f = fixture(t, '');
  assert.match(checkConsoleScreens(f.source, f.docs, f.site).join('\n'), /no console navigation/i);
});

test('navigation in the common fragment requires its screenshot', (t) => {
  const f = fixture(t);
  assert.match(checkConsoleScreens(f.source, f.docs, f.site).join('\n'), /agents\.png is missing/);
});

test('a screenshot does not replace the required guide section', (t) => {
  const f = fixture(t);
  writeFileSync(join(f.site, 'public/screenshots/agents.png'), 'fixture');
  writeFileSync(join(f.docs, 'ui.md'), '# Console\n');
  assert.match(checkConsoleScreens(f.source, f.docs, f.site).join('\n'), /no section describing/);
});

test('a documented screen and its image pass with either key quote style', (t) => {
  const f = fixture(t, "'nav.agents': 'Agents', 'nav.primary': 'Primary',");
  writeFileSync(join(f.site, 'public/screenshots/agents.png'), 'fixture');
  assert.deepEqual(checkConsoleScreens(f.source, f.docs, f.site), []);
});
