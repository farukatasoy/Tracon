import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { checkConsoleScreens, computeUiSourceHash } from './check-console-screens.mjs';

const AGENTS_ENTRY =
  "{ path: 'agents', label: 'nav.agents', english: 'Agents', icon: AgentsIcon },";

function fixture(t, entries = AGENTS_ENTRY) {
  const root = mkdtempSync(join(tmpdir(), 'tracon-screenshot-gate-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const source = join(root, 'src');
  const docs = join(root, 'docs');
  const site = join(root, 'site');
  const components = join(source, 'Tracon.UI/frontend/src/components');
  mkdirSync(components, { recursive: true });
  mkdirSync(docs, { recursive: true });
  mkdirSync(join(site, 'public/screenshots'), { recursive: true });
  writeFileSync(
    join(components, 'navigation.ts'),
    `export const NAV_GROUPS = [{ group: 'nav.group.operate', items: [${entries}] }];`,
  );
  writeFileSync(join(source, 'Tracon.UI/frontend/package.json'), '{}');
  writeFileSync(join(source, 'Tracon.UI/frontend/package-lock.json'), '{}');
  writeFileSync(join(docs, 'ui.md'), '## Agents\n\nThe agent catalog.\n');
  writeFileSync(
    join(site, 'public/screenshots/.ui-source.sha256'),
    `${computeUiSourceHash(source)}\n`,
  );
  return { source, docs, site };
}

function refreshStamp(f) {
  writeFileSync(
    join(f.site, 'public/screenshots/.ui-source.sha256'),
    `${computeUiSourceHash(f.source)}\n`,
  );
}

test('an empty navigation inventory fails instead of checking zero screenshots', (t) => {
  const f = fixture(t, '');
  assert.match(checkConsoleScreens(f.source, f.docs, f.site).join('\n'), /no console navigation/i);
});

test('a navigation entry requires its screenshot', (t) => {
  const f = fixture(t);
  assert.match(checkConsoleScreens(f.source, f.docs, f.site).join('\n'), /agents\.png is missing/);
});

test('a screenshot does not replace the required guide section', (t) => {
  const f = fixture(t);
  writeFileSync(join(f.site, 'public/screenshots/agents.png'), 'fixture');
  writeFileSync(join(f.docs, 'ui.md'), '# Console\n');
  assert.match(checkConsoleScreens(f.source, f.docs, f.site).join('\n'), /no section describing/);
});

test('a documented screen and its image pass', (t) => {
  const f = fixture(t);
  writeFileSync(join(f.site, 'public/screenshots/agents.png'), 'fixture');
  assert.deepEqual(checkConsoleScreens(f.source, f.docs, f.site), []);
});

test('a nav.* key that is not in the table demands nothing', (t) => {
  // `shell.skipToContent` and friends are message keys, not screens. Reading
  // the catalogue treated every `nav.*` key as a screen and made a shell
  // affordance demand a screenshot of itself. The table is the inventory.
  const f = fixture(t);
  writeFileSync(join(f.site, 'public/screenshots/agents.png'), 'fixture');
  writeFileSync(
    join(f.source, 'Tracon.UI/frontend/src/components/navigation.ts'),
    `// A stray reference to the message key 'nav.skipToContent' in a comment.\n` +
      `export const NAV_GROUPS = [{ group: 'nav.group.operate', items: [${AGENTS_ENTRY}] }];`,
  );
  refreshStamp(f);

  assert.deepEqual(checkConsoleScreens(f.source, f.docs, f.site), []);
});

test('a UI source change makes the screenshot set stale', (t) => {
  const f = fixture(t);
  writeFileSync(join(f.site, 'public/screenshots/agents.png'), 'fixture');
  writeFileSync(
    join(f.source, 'Tracon.UI/frontend/src/components/navigation.ts'),
    `${AGENTS_ENTRY}\n// changed after capture`,
  );

  assert.match(checkConsoleScreens(f.source, f.docs, f.site).join('\n'), /do not match the current UI source/i);
});

test('an entry written in a different property order fails loudly instead of vanishing', (t) => {
  // The regex needs path → label → english. A reordered entry used to parse as
  // nothing at all, which silently stopped requiring that screen's screenshot.
  const f = fixture(t, "{ english: 'Agents', path: 'agents', label: 'nav.agents', icon: AgentsIcon },");
  writeFileSync(join(f.site, 'public/screenshots/agents.png'), 'fixture');

  assert.match(
    checkConsoleScreens(f.source, f.docs, f.site).join('\n'),
    /declares 1 screens but only 0 parsed/,
  );
});
