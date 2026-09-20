import { createHash } from 'node:crypto';
import { readFileSync, existsSync, readdirSync } from 'node:fs';
import { join, relative, sep } from 'node:path';

const screenshotStamp = '.ui-source.sha256';

/** Hash every input that can change the rendered console screenshots. */
export function computeUiSourceHash(sourceRoot) {
  const frontendRoot = join(sourceRoot, 'Tracon.UI/frontend');
  const files = [join(frontendRoot, 'package.json'), join(frontendRoot, 'package-lock.json')];
  const pending = [join(frontendRoot, 'src')];

  while (pending.length > 0) {
    const directory = pending.pop();

    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isDirectory()) pending.push(path);
      else files.push(path);
    }
  }

  const hash = createHash('sha256');
  for (const file of files.sort()) {
    // The stamp is committed and checked on every supported runner. Native
    // relative paths use `\\` on Windows and `/` elsewhere, so hashing the raw
    // path made identical source trees produce platform-specific stamps.
    hash.update(relative(frontendRoot, file).split(sep).join('/'));
    hash.update('\0');
    hash.update(readFileSync(file));
    hash.update('\0');
  }

  return hash.digest('hex');
}

/**
 * Every console screen must have a section in the guide and a screenshot.
 *
 * The inventory is read from the console's own navigation table, which is the
 * single list the sidebar and the command palette both render. Reading the
 * message catalogue instead made every `nav.*` key look like a screen, so a
 * shell affordance that happened to live in that namespace demanded a
 * screenshot of itself.
 */
export function checkConsoleScreens(sourceRoot, docsRoot, siteRoot) {
  const errors = [];
  const navigation = readFileSync(
    join(sourceRoot, 'Tracon.UI/frontend/src/components/navigation.ts'),
    'utf8',
  );
  const uiGuide = readFileSync(join(docsRoot, 'ui.md'), 'utf8');
  const screenshotRoot = join(siteRoot, 'public/screenshots');
  const stampPath = join(screenshotRoot, screenshotStamp);
  const expectedStamp = computeUiSourceHash(sourceRoot);

  if (!existsSync(stampPath) || readFileSync(stampPath, 'utf8').trim() !== expectedStamp) {
    errors.push(
      `Console screenshots do not match the current UI source. Regenerate them and run ` +
        '`node scripts/refresh-console-screenshot-stamp.mjs` from docs-site.',
    );
  }

  // `{ path: 'runs', label: 'nav.runs', english: 'Runs', ... }` — one entry per
  // screen, in the order the console shows them.
  const screens = [
    ...navigation.matchAll(/path:\s*'([a-z-]+)',\s*label:\s*'nav\.[A-Za-z]+',\s*english:\s*'([^']+)'/g),
  ];

  if (screens.length === 0) {
    errors.push('No console navigation entries found; refusing to validate zero screenshots.');
  }

  // 🚨 The pattern above depends on the order the three properties are written
  // in. A reordered entry would parse as zero screens and silently stop
  // demanding its section and its screenshot — the exact failure mode this gate
  // was rewritten to close. Count the entries independently and compare.
  // Counted on the one property every screen entry carries exactly once, and
  // which no group label can imitate: a group is `nav.group.<name>`, and the
  // pattern below requires the closing quote straight after the letters.
  const declared = [...navigation.matchAll(/label:\s*'nav\.[A-Za-z]+'/g)].length;

  if (screens.length !== declared) {
    errors.push(
      `navigation.ts declares ${declared} screens but only ${screens.length} parsed. ` +
        "An entry must read { path: '…', label: 'nav.…', english: '…', … } in that order.",
    );
  }

  for (const [, path, label] of screens) {
    // A heading, not a passing mention: 'Jobs' appears in a cross-link on a page that
    // never describes the Jobs screen, which is exactly the gap this gate closes.
    if (!new RegExp(`^#{2,3} .*\\b${label}\\b`, 'im').test(uiGuide)) {
      errors.push(`ui.md has no section describing the '${label}' console screen`);
    }

    if (!existsSync(join(screenshotRoot, `${path}.png`))) {
      errors.push(
        `public/screenshots/${path}.png is missing; regenerate with ` +
          'TRACON_UI_SCREENSHOTS=1 dotnet test tests/Tracon.Ui.E2ETests -c Release',
      );
    }
  }

  return errors;
}
