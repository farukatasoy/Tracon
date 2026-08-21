// What one documentation page costs a reader, measured rather than assumed.
//
// This phase added a token layer, a section rule, diagrams to nine guides, and a
// symptom index. None of that is free, and nothing was measuring it. The ceiling
// below is the measured worst page plus headroom — it is a ratchet against silent
// growth, not a target anybody has to design against.
//
// What is counted: the page's own HTML, plus every stylesheet and script the HTML
// references, each gzipped. What is not: mermaid's diagram parser. It is loaded on
// demand by the pages that have a diagram and is already justified by a measured
// chunk limit in astro.config.mjs; folding it in here would charge every page for a
// download only some of them make, and would hide the number this gate exists for.

import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync } from 'node:zlib';

import { base } from '../site.config.mjs';

const here = resolve(fileURLToPath(new URL('.', import.meta.url)));
const dist = resolve(here, '../dist');

// Measured 2026-08-20: the heaviest page is troubleshooting at 49 365 B, which is
// also the largest source page on the site. The ceiling adds about 15% of headroom.
const CEILING = 57_000;

if (!existsSync(dist)) {
  console.error('dist/ is missing; run `npm run build` before checking page weight.');
  process.exit(1);
}

const gzip = (buffer) => gzipSync(buffer, { level: 9 }).length;
const assetCache = new Map();

function assetWeight(url) {
  if (!assetCache.has(url)) {
    const path = join(dist, url.slice(base.length));

    // Counting an unresolved asset as zero would turn a moved file, or a changed
    // `base`, into a page that suddenly measures HTML-only and passes. A gate that
    // cannot make its measurement says so.
    if (!existsSync(path)) {
      console.error(`Cannot weigh ${url}: no file at ${relative(dist, path)}.`);
      process.exit(1);
    }

    assetCache.set(url, gzip(readFileSync(path)));
  }

  return assetCache.get(url);
}

const pages = collect(dist).filter((file) => file.endsWith('.html'));
const over = [];
let heaviest = { page: '', weight: 0 };

for (const page of pages) {
  const html = readFileSync(page);
  const text = html.toString('utf8');
  const assets = new Set(
    [...text.matchAll(/(?:href|src)="([^"]*\.(?:css|js))"/g)]
      .map(([, url]) => url)
      // `base` is '/', so the prefix test alone would also claim protocol-relative
      // addresses (`//cdn.example/x.css`), which resolve to no local file and would
      // trip the unresolved-asset failure below.
      .filter((url) => !url.startsWith('//') && url.startsWith(base)),
  );

  let weight = gzip(html);
  for (const asset of assets) weight += assetWeight(asset);

  if (weight > heaviest.weight) {
    heaviest = { page: relative(dist, page), weight };
  }

  if (weight > CEILING) {
    over.push(`${relative(dist, page)}: ${weight} B gzip, ceiling ${CEILING} B`);
  }
}

if (over.length > 0) {
  console.error(`${over.length} page(s) over the weight ceiling:`);
  for (const entry of over.slice(0, 20)) console.error(`  ${entry}`);
  console.error('\nEither reduce the page, or raise CEILING in this file with the measurement.');
  process.exit(1);
}

console.log(
  `Weight: ${pages.length} pages under ${CEILING} B gzip. ` +
    `Heaviest: ${heaviest.page} at ${heaviest.weight} B.`,
);

function collect(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? collect(path) : [path];
  });
}
