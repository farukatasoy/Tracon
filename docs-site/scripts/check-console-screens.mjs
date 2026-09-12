import { readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';

export function checkConsoleScreens(sourceRoot, docsRoot, siteRoot) {
  const errors = [];
  const consoleLocales = readFileSync(
    join(sourceRoot, 'Tracon.UI/frontend/src/locales/en/common.ts'),
    'utf8',
  );
  const uiGuide = readFileSync(join(docsRoot, 'ui.md'), 'utf8');
  const screenshotRoot = join(siteRoot, 'public/screenshots');

  const screens = [...consoleLocales.matchAll(/["']nav\.([A-Za-z]+)["']:\s*['"]([^'"]+)['"]/g)].filter(([, key]) => key !== 'primary');
  if (screens.length === 0) errors.push('No console navigation entries found; refusing to validate zero screenshots.');
  for (const [, key, label] of screens) {
    // A heading, not a passing mention: 'Jobs' appears in a cross-link on a page that
    // never describes the Jobs screen, which is exactly the gap this gate closes.
    if (!new RegExp(`^#{2,3} .*\\b${label}\\b`, 'im').test(uiGuide)) {
      errors.push(`ui.md has no section describing the '${label}' console screen`);
    }

    if (!existsSync(join(screenshotRoot, `${key}.png`))) {
      errors.push(
        `public/screenshots/${key}.png is missing; regenerate with ` +
          'TRACON_UI_SCREENSHOTS=1 dotnet test tests/Tracon.Ui.E2ETests -c Release',
      );
    }
  }

  return errors;
}
