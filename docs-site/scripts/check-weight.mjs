// What one documentation page costs a reader, measured rather than assumed.
//
// A token layer, a section rule, diagrams and a symptom index are not free, and
// nothing was measuring them. The ceiling below is the measured worst page plus
// headroom — it is a ratchet against silent growth, not a target anybody has to
// design against.
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

// The ceiling is the measured worst page plus headroom. Last measured 2026-11-10: concepts/runs, the
// heaviest page, at 59 367 B gzip on a local build, 133 B under the ceiling. A CI build of the same commit
// weighs about 56 B more than a local one (every page also carries the same 23 925 B of shared assets), so the
// headroom must never shrink below that gap.
//
// When a page crosses the ceiling, split it instead of raising the number. Every earlier crossing was settled
// that way (the build diagnostics, voice, live voice, function nodes, upgrades and the package-level
// configuration each moved to a page of their own); the ceiling was raised only when a page sat within a few
// bytes of it and nothing could move without orphaning inbound links. Split the heaviest page along a topic of
// its own that has few inbound links. The history of every raise and split is in
// `git log -p -- docs-site/scripts/check-weight.mjs`.
const CEILING = 59_500;

// The release notes were one generated page that grew with every release, not with what a reader
// needs from one visit: a reader lands on one version's anchor (each package's PackageReleaseNotes
// links there). It crossed its ceiling twice - 97 534 B at the 1.0.0-preview.4 cut (ceiling 110 000 B),
// 131 792 B at the 1.0.0-preview.6 cut (raised to 150 000 B once, with the split promised for the
// next release). Split 2026-10-29: `reference/changelog/` is the index (a summary and a link
// per version, under CEILING) and every release has its own page under `reference/releases/`.
// Measured the same day: 1.0.0-preview.6 stays under CEILING; 1.0.0-preview.4 does not, at 75 956 B
// (its section alone is 131 194 B of Markdown, 44 221 B gzipped, on top of the 23 925 B every page
// shares). That release is closed: its section cannot grow, so the exception is a ceiling for that
// one page, 80 000 B, about 5% headroom, and not a pattern. A later release that crosses CEILING is
// split or measured the same way.
//
// 1.0.0-preview.8 crosses CEILING too, and by more: measured 2026-11-08 at 129 070 B gzip locally (its section is
// 256 786 B of Markdown with 249 entries, 111 of them under Fixed, on top of the 23 925 B every page shares). The
// cut is the moment it becomes a closed release, so the same rule applies: a ceiling for that one page, 135 000 B,
// about 4.6% over the measured page, which also covers the roughly 60 B by which a CI build differs from a local
// one. It was not split at the cut because the page is the unit each package's release-notes link lands on, and a
// split changes that link of every package of the release; the next release that crosses CEILING is split, and the
// generator is the place to do it (one page per section of a version).
const PAGE_CEILINGS_RELEASE = [
  ['reference/releases/1.0.0-preview.4/index.html', 80_000],
  ['reference/releases/1.0.0-preview.8/index.html', 135_000],
];

// The generated API page of TraconBuilderExtensions is the second page that grows with the product and
// not with what a reader needs from one visit: DocFX writes one page per type, and every capability of the
// Tracon chain is a method of this one static class. Measured 2026-10-28: 61 137 B gzip after
// the page took AddJobHandler and UseScheduling from IServiceCollection, the model-declaring forms of
// AddAgent (each a pair, because the public API analyzer refuses two overloads with an optional parameter)
// and the factory forms of AddTool - eleven methods with their remarks and examples. The description-taking
// member of each AddAgent pair already says "See ..." and carries no example of its own, so what the new
// members add is the contract a consumer reads, not repeated prose. A static class cannot be split across
// pages. Ceiling 64 000 B, about 4.7% headroom, which also covers the roughly 60 B by which a CI build
// differs from a local one.
//
// The generated API page of RunStoreContract is the third: every scenario a store must pass is a public method of this
// one abstract class (the test runner finds it by inheritance), and DocFX writes one page per type. Measured
// 2026-11-06: 59 569 B gzip, 69 B over CEILING, after the contract took two scenarios (a queued run that has just started
// running is not closed; a second start leaves the heartbeat the run has), each a name and a one-line summary. The
// page cannot be split: a store inherits one class. Ceiling 62 500 B, about 4.9% headroom, which also covers the roughly
// 60 B by which a CI build differs from a local one. The page that grows with every new rule of the run store is a
// reason to read the contract's source, not the page; the next raise should say so in the page's own summary.
const PAGE_CEILINGS = new Map([
  ...PAGE_CEILINGS_RELEASE,
  ['api/tracon.traconbuilderextensions/index.html', 64_000],
  ['api/tracon.testing.contracts.storage.runstorecontract/index.html', 62_500],
]);

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

  const name = relative(dist, page).split('\\').join('/');
  const ceiling = PAGE_CEILINGS.get(name) ?? CEILING;

  if (!PAGE_CEILINGS.has(name) && weight > heaviest.weight) {
    heaviest = { page: name, weight };
  }

  if (weight > ceiling) {
    over.push(`${name}: ${weight} B gzip, ceiling ${ceiling} B`);
  }
}

if (over.length > 0) {
  console.error(`${over.length} page(s) over the weight ceiling:`);
  for (const entry of over.slice(0, 20)) console.error(`  ${entry}`);
  console.error('\nEither reduce the page, or raise CEILING in this file with the measurement.');
  process.exit(1);
}

console.log(
  `Weight: ${pages.length} pages under ${CEILING} B gzip ` +
    `(${[...PAGE_CEILINGS].map(([page, limit]) => `${page} under ${limit} B`).join(', ')}). ` +
    `Heaviest of the rest: ${heaviest.page} at ${heaviest.weight} B.`,
);

function collect(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? collect(path) : [path];
  });
}
