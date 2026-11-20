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

// The ceiling is the measured worst page plus headroom. Last measured 2026-11-12: concepts/runs, the
// heaviest page, at 59 381 B gzip on a local build, 119 B under the ceiling (concepts/governance, the heaviest before
// its Webhooks section became a page of its own, now weighs 57 860 B). A CI build of the same commit
// weighs more than a local one (every page also carries the same 23 925 B of shared assets): about 56 B more
// was measured on 2026-11-12 and 97 B more on 2026-11-14, when concepts/runs weighed 59 495 B here and 59 592 B
// in CI, 5 B under the ceiling locally and 92 B over it on the runner, and the public build went red while this
// gate was green. The gap differs from page to page, so the headroom a page keeps is a rule of its own
// (MIN_HEADROOM below) and the gate fails on it; it was a sentence in this comment until a page was squeezed
// under the ceiling by trimming a sentence twice.
//
// When a page crosses the ceiling, split it instead of raising the number. Every earlier crossing was settled
// that way (the build diagnostics, voice, live voice, function nodes, upgrades and the package-level
// configuration each moved to a page of their own); the ceiling was raised only when a page sat within a few
// bytes of it and nothing could move without orphaning inbound links. Split the heaviest page along a topic of
// its own that has few inbound links. The history of every raise and split is in
// `git log -p -- docs-site/scripts/check-weight.mjs`.
const CEILING = 59_500;

// What a CI build adds to a page, measured (97 B, see above), and the headroom every page keeps under its ceiling:
// twice the measured gap, because the gap is not the same on every page. A page that fails this is split along a
// topic of its own; trimming a sentence to get back under only moves the next edit to the next failure.
const CI_BUILD_GAP = 100;
const MIN_HEADROOM = 2 * CI_BUILD_GAP;

// The release notes used to be one page per release, and the pages of 1.0.0-preview.4 (80 000 B) and 1.0.0-preview.8
// (135 000 B) each carried a ceiling of their own after they crossed this one, with the promise that the next release
// to cross it would be split. It was: `build-changelog.mjs` cuts a release whose Markdown does not fit one page into
// parts at the boundary of an entry (`changelog-split.mjs`), so no release page needs a ceiling of its own and a
// release that grows adds a part, not an exception.

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
//
// Raised to 67 500 B on 2026-11-13: 64 356 B gzip, 1 856 B over the ceiling above, after the contract took the promotion of a
// queued run (`PromoteQueuedRunAsync`: five scenarios and the flag `PromotesQueuedRunsAtomically`) and the flag
// `MarksAStartedRunAlive` with its scenario. Eight public members, each a name and a one-line summary, and the class summary
// now says that the page grows and where to read instead. About 4.9% headroom, which also covers the roughly 60 B by which a CI
// build differs from a local one.
// Raised to 67 500 B on 2026-11-20: 64 719 B gzip, 719 B over the ceiling above, after the class took
// AddChatClientProvider (four overloads, because the public API analyzer refuses two overloads with an optional
// parameter; the three that take a catalog or a factory carry a summary and a pointer to the first). About 4.3%
// headroom, which also covers the roughly 60 B by which a CI build differs from a local one. A static class
// cannot be split across pages.
const PAGE_CEILINGS = new Map([
  ['api/tracon.traconbuilderextensions/index.html', 67_500],
  ['api/tracon.testing.contracts.storage.runstorecontract/index.html', 67_500],
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
const tight = [];
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
  } else if (ceiling - weight < MIN_HEADROOM) {
    tight.push(`${name}: ${weight} B gzip, ${ceiling - weight} B under its ${ceiling} B ceiling`);
  }
}

if (over.length > 0) {
  console.error(`${over.length} page(s) over the weight ceiling:`);
  for (const entry of over.slice(0, 20)) console.error(`  ${entry}`);
  console.error('\nEither reduce the page, or raise CEILING in this file with the measurement.');
  process.exit(1);
}

if (tight.length > 0) {
  console.error(`${tight.length} page(s) under the ceiling by less than ${MIN_HEADROOM} B:`);
  for (const entry of tight.slice(0, 20)) console.error(`  ${entry}`);
  console.error(
    `\nA CI build weighs up to ${CI_BUILD_GAP} B more than this one, so these pages pass here and fail there. ` +
      'Split the page along a topic of its own (see the comment above CEILING); do not trim it to fit.',
  );
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
