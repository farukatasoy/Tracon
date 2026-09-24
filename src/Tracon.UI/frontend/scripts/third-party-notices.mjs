// Third-party notices for the code the UI bundle actually ships (BL-058).
//
// Tracon.UI embeds the Vite output in its assembly, so every consumer who
// deploys Tracon.UI.dll redistributes React, React DOM, TanStack Query and the
// rest. Their licences (MIT) require the notice to travel with every copy.
// Vite drops licence comments, so before this file the package carried their
// code and none of their notices.
//
// Two halves:
//   1. `thirdPartyModules(build)` - a Vite plugin. It records the package of
//      every module that RENDERED into the output (renderedLength > 0), so a
//      dependency that is declared but tree-shaken away is not listed and a
//      transitive one that is bundled is. The graph is the truth; the
//      package-lock is only what COULD ship.
//   2. `writeThirdPartyNotices()` - runs last in `npm run build`. It reads the
//      records of every build, renders the notice, and fails when the
//      checked-in `src/Tracon.UI/THIRD-PARTY-NOTICES.txt` differs. It then
//      copies the CHECKED-IN bytes into wwwroot/, so the packed file, the
//      embedded resource and the repository file are the same bytes
//      (`ReleaseArtifactTests.UiPackageCarriesThirdPartyNotices`).
//
// Update after a dependency change (from src/Tracon.UI/frontend):
//   npm run build  ->  node scripts/third-party-notices.mjs --write  ->  commit

import { copyFileSync, existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const FRONTEND_ROOT = resolve(import.meta.dirname, '..');
const PROJECT_ROOT = resolve(FRONTEND_ROOT, '..');

export const NOTICE_NAME = 'THIRD-PARTY-NOTICES.txt';

const CHECKED_IN_NOTICE = join(PROJECT_ROOT, NOTICE_NAME);
const EMBEDDED_NOTICE = join(PROJECT_ROOT, 'wwwroot', NOTICE_NAME);
const RECORD_DIR = join(FRONTEND_ROOT, 'node_modules', '.tracon-notices');

/** Every Vite build that writes into wwwroot/. Each one leaves a record. */
const BUILDS = ['console', 'embed'];

const LICENCE_FILE = /^(licen[cs]e|copying)(\.(md|txt))?$/i;
const NODE_MODULES = '/node_modules/';

/** `.../node_modules/<name>` or `.../node_modules/@scope/<name>` of a module id, or null. */
function packageRootOf(moduleId) {
  const path = moduleId.replace(/^\0/, '').split('?')[0].replaceAll('\\', '/');
  const at = path.lastIndexOf(NODE_MODULES);

  if (at === -1) {
    return null;
  }

  const segments = path.slice(at + NODE_MODULES.length).split('/');
  const nameLength = segments[0].startsWith('@') ? 2 : 1;

  return path.slice(0, at + NODE_MODULES.length) + segments.slice(0, nameLength).join('/');
}

/**
 * Records the packages whose code this build emitted.
 *
 * `css` names packages that reach the CSS output WITHOUT a module in the graph:
 * `@tailwindcss/vite` inlines Tailwind's own preflight and theme, so the graph
 * never shows `tailwindcss`. They are listed only when the build emits CSS.
 */
export function thirdPartyModules(build, { css = [] } = {}) {
  if (!BUILDS.includes(build)) {
    throw new Error(`Unknown build '${build}'; add it to BUILDS in scripts/third-party-notices.mjs.`);
  }

  return {
    name: 'tracon-third-party-modules',
    apply: 'build',
    generateBundle(_options, bundle) {
      const roots = new Set();
      let emitsCss = false;

      for (const output of Object.values(bundle)) {
        if (output.type === 'asset' && output.fileName.endsWith('.css')) {
          emitsCss = true;
        }

        if (output.type !== 'chunk') {
          continue;
        }

        for (const [id, module] of Object.entries(output.modules)) {
          const root = module.renderedLength > 0 ? packageRootOf(id) : null;

          if (root !== null) {
            roots.add(root);
          }
        }
      }

      if (emitsCss) {
        for (const name of css) {
          const root = join(FRONTEND_ROOT, 'node_modules', name);

          if (!existsSync(join(root, 'package.json'))) {
            throw new Error(`'${name}' reaches the CSS output but is not installed at ${root}.`);
          }

          roots.add(root.replaceAll('\\', '/'));
        }
      }

      mkdirSync(RECORD_DIR, { recursive: true });
      writeFileSync(join(RECORD_DIR, `${build}.json`), JSON.stringify([...roots].sort(), null, 2));
    },
  };
}

function readPackage(root) {
  const manifest = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8'));
  const licenceFile = readdirSync(root).find((entry) => LICENCE_FILE.test(entry));

  if (licenceFile === undefined) {
    throw new Error(`${manifest.name} ships in the UI bundle but has no licence file in ${root}; its notice cannot ship.`);
  }

  const licence = typeof manifest.license === 'string' ? manifest.license : (manifest.license?.type ?? 'UNKNOWN');
  const text = readFileSync(join(root, licenceFile), 'utf8').replaceAll('\r\n', '\n').trim();

  return { name: manifest.name, licence, text };
}

/** The notice text for every package the recorded builds emitted. */
export function renderThirdPartyNotices() {
  const roots = new Set();

  for (const build of BUILDS) {
    const record = join(RECORD_DIR, `${build}.json`);

    if (!existsSync(record)) {
      throw new Error(`No module record for the '${build}' build (${record}). Run the whole \`npm run build\` first.`);
    }

    for (const root of JSON.parse(readFileSync(record, 'utf8'))) {
      roots.add(root);
    }
  }

  // Two copies of one package (a nested install) are one notice.
  const packages = new Map();

  for (const root of [...roots].sort()) {
    const entry = readPackage(root);

    if (!packages.has(entry.name)) {
      packages.set(entry.name, entry);
    }
  }

  const rule = '='.repeat(80);
  const sections = [...packages.values()]
    .sort((left, right) => (left.name < right.name ? -1 : left.name > right.name ? 1 : 0))
    .map((entry) => [rule, `${entry.name} (${entry.licence})`, rule, '', entry.text, ''].join('\n'));

  return [
    'Tracon.UI - third-party notices',
    '',
    'Tracon.UI embeds a JavaScript and CSS bundle in its assembly. That bundle',
    'contains code from the packages below. Each notice is reproduced as the',
    'package ships it.',
    '',
    'Generated by src/Tracon.UI/frontend/scripts/third-party-notices.mjs from',
    'the modules the build emitted. Do not edit by hand.',
    '',
    ...sections,
  ].join('\n');
}

/**
 * Fails when the checked-in notice no longer matches the bundle; `write`
 * replaces it instead. Either way wwwroot/ receives the checked-in bytes.
 */
export function writeThirdPartyNotices({ write = false } = {}) {
  const expected = renderThirdPartyNotices();

  if (write) {
    writeFileSync(CHECKED_IN_NOTICE, expected);
  } else {
    const current = existsSync(CHECKED_IN_NOTICE) ? readFileSync(CHECKED_IN_NOTICE, 'utf8').replaceAll('\r\n', '\n') : null;

    if (current !== expected) {
      throw new Error(
        `src/Tracon.UI/${NOTICE_NAME} does not match the packages the UI bundle ships. ` +
          'From src/Tracon.UI/frontend run: node scripts/third-party-notices.mjs --write, then commit the file.',
      );
    }
  }

  mkdirSync(dirname(EMBEDDED_NOTICE), { recursive: true });
  copyFileSync(CHECKED_IN_NOTICE, EMBEDDED_NOTICE);

  return expected;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  writeThirdPartyNotices({ write: process.argv.includes('--write') });
  process.stdout.write(`${NOTICE_NAME} written\n`);
}
