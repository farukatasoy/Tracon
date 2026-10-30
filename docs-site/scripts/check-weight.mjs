// What one documentation page costs a reader, measured rather than assumed.
//
// This change added a token layer, a section rule, diagrams to nine guides, and a
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
//
// Re-measured 2026-09-12: the same page reached 57 367 B and crossed the 57 000
// ceiling. The growth is one deliberate addition, not drift — the brand contract
// requires every page showing an install command to carry the "not published yet"
// warning, and this page shows one. That aside costs exactly 376 B gzip here,
// almost all of it the icon and wrapper markup Starlight renders: shortening its
// text back saves only 95 B. The alternatives were measured before the ceiling
// moved — no dead rule is left in site.css (all four unused-looking classes are
// live, `pagefind-ui` at run time), and dropping the warning would have landed at
// 56 991 B, nine bytes under, which is not a margin. Raised to 58 000 B, which
// keeps about 1% of headroom over the measured page.
//
// Re-measured 2026-09-16: the same page reached 58 391 B, and the 58 000 ceiling
// had 15 B of headroom left before this change - anything added to this page
// would have crossed it. The addition is one more diagnostic section, TRC0403,
// which the page cannot omit: it is the only page that explains all of them, and
// a gate test enforces that. Costs were measured rather than estimated: the
// section is 450 B gzip and its row in the summary table 36 B, and shortening the
// prose recovered 80 B of the 450. Raised to 59 000 B, which restores about 1% of
// headroom. This page is the site's largest by a wide margin and grows with every
// diagnostic; the next raise should split it instead.
//
// Split 2026-09-24 instead of raised: CI measured the page at 59 003 B while a
// local build of the same commit measured 58 993 B - the two environments differ
// by about ten bytes, so a margin of a few bytes is no margin. The Voice symptoms
// moved to guides/voice.md (#troubleshooting), next to the feature, and the page
// dropped to 58 151 B. Ceiling unchanged.
//
// Split again 2026-10-17: the page was back at 58 994 B, 6 B under the ceiling, and the
// next symptom entry could not fit. The build-diagnostics section (twenty headings, one per
// TRC id and agent-map symptom) moved to guides/build-diagnostics.md; troubleshooting dropped
// to 49 054 B and the new page weighs 43 648 B. The heaviest page is now guides/production.
// Ceiling unchanged.
//
// Split three pages 2026-10-27 (1.0.0-preview.6): CI measured concepts/workflows at 59 039 B
// and guides/voice at 59 007 B while a local build of the same commit measured workflows at
// 58 983 B - the gap between the two environments has grown to about 56 B, and every page
// shares the same 23 925 B of assets. Function nodes moved to concepts/workflow-function-nodes,
// provider-hosted live voice to guides/live-voice, and the three upgrade sections of
// guides/production (which the new sidebar entries had pushed to 58 985 B locally) to
// guides/production-upgrades. The heaviest page is now concepts/governance at 58 194 B
// locally. Ceiling unchanged.
//
// Raised 2026-10-30 from 59 000 to 59 200 B instead of split: a new guide's sidebar entry is on
// every page, about 45 B gzip, and pushed concepts/governance from about 58 975 B (25 B under the ceiling
// before the entry) to 59 032 B locally. The sidebar label was shortened ("Move definitions", -14 B); trimming
// filler words out of the page itself recovered 3 B, because gzip already removes them, so the page was
// restored. 200 B is about 0.3% headroom, which covers the 56 B gap between CI and a local build and one more
// sidebar entry. The next raise should split the page, as the three entries above did.
const CEILING = 59_200;

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
const PAGE_CEILINGS_RELEASE = [['reference/releases/1.0.0-preview.4/index.html', 80_000]];

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
const PAGE_CEILINGS = new Map([
  ...PAGE_CEILINGS_RELEASE,
  ['api/tracon.traconbuilderextensions/index.html', 64_000],
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
