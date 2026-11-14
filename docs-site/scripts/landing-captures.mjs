// The home page's console crops, declared once: which E2E screenshot each crop
// comes from, the region it frames, the widths it is written at, and the exact
// screenshot it was framed on.
//
// build-landing-crops.mjs writes the files; check-content.mjs checks the same
// facts without writing anything, so a pull request that recaptures the console
// or renames a crop fails in the content gate that CI runs on every PR, not first
// at deploy. Only node:crypto and node:fs are used here for that reason: the
// content gate runs without `npm ci`.

import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

// A recapture (TRACON_UI_SCREENSHOTS=1) changes these files. Put the new hash
// here only after opening public/landing/<crop>*.avif and confirming the crop
// still frames what CROPS says it frames.
export const CAPTURE_PINS = {
  'approvals.png': '93c1131d00599ad0a0eca0c85fc10cf7dfd3822417e41906687f8e318000571e',
  'approvals-light.png': '2cd6f8ca57c285b41564fb08e0f39bc6c8385f2e6ffa5f9358734a02c712722f',
  'run-detail.png': '2f6a04b188fe52d0d84fc4524b47def8eaef908b5a1d6467abbc6d482a6fac33',
  'run-detail-light.png': 'de8278d43933cd963bb64bc2119421253ae04d37b10fa0bcc20b48010a4b1e40',
  'runs.png': '9598a91f65b59a39739a3aff6db6aa422a68c1ba4674eec37dbdc211f322f8a8',
  'runs-light.png': 'd8dfd2defa162d35693d05d51a1670cdb6710f62e3d8d65120c47c315c9ac7f0',
  'agent-detail.png': '2f34cabce342966571b329f46d1d6a9d5b27f50c47c8293f7d3aeb9389ad4671',
  'agent-detail-light.png': 'acbbc7d219ea0f0e9dbf6701881b8211ffd1e03933c8a23e4b26d46afecab341',
  'workflows.png': '923b1b7df5ff03327c122eb4d84d52ac0cfb19f64b9c1062e9c6f17530f0295f',
  'workflows-light.png': '5dee26c72e8deb4a430dc8197032f07de4385671abdb73b8618013531ab0801e',
  'jobs.png': '21e9aa79c4d58e573173393535f7ef9ef7ff9649585b7c7c21779d7db40b4654',
  'jobs-light.png': 'b064fa6b473cbb9bcd73e5c2f78a06299b244d4adfc2f8986bf27c60207b1bb6',
};

// Regions in source pixels (the captures are 2x). They start below the console's
// top bar, so the build version it prints never reaches the home page. A crop is
// shown only where it renders at two thirds of 1:1 or larger; index.mdx switches
// the wide and narrow crops at the widths that keep that true. The wide crops'
// 1x width also gets a WebP: it is the <img> fallback for a browser without AVIF.
export const CROPS = [
  {
    name: 'approvals-wide',
    source: 'approvals',
    frames: 'the Approvals heading, its note, and the cancel_order row for ORD-7 with Approve and Reject',
    region: { left: 536, top: 118, width: 2224, height: 448 },
    widths: [1112, 2224],
    fallback: true,
  },
  {
    name: 'approvals-narrow',
    source: 'approvals',
    frames: 'the TOOL cell of the cancel_order row: Order ORD-7, cancel_order, its message',
    region: { left: 536, top: 330, width: 620, height: 236 },
    widths: [310, 620],
  },
  {
    name: 'run-transcript-wide',
    source: 'run-detail',
    frames: 'the transcript with reserve_stock marked failed and its argument orderId=ORD-9',
    region: { left: 548, top: 1490, width: 1086, height: 310 },
    widths: [543, 1086],
    fallback: true,
  },
  {
    name: 'run-transcript-narrow',
    source: 'run-detail',
    frames: 'the same failed reserve_stock call, narrower',
    region: { left: 548, top: 1490, width: 872, height: 310 },
    widths: [436, 872],
  },
  // The four console strip crops are about 1000 source pixels wide: 500 to 520 px
  // of the real console, so a 343 px column (a 375 px viewport) still shows them
  // at two thirds of 1:1. The jobs crop stops before the cron column, whose
  // heading and first cell would otherwise be cut in half.
  {
    name: 'console-runs',
    source: 'runs',
    frames: 'the runs table: six rows with run id, agent and status, one awaiting approval and one awaiting input',
    region: { left: 544, top: 636, width: 1040, height: 460 },
    widths: [520, 1040],
    fallback: true,
  },
  {
    name: 'console-agent',
    source: 'agent-detail',
    frames: 'the Summary panel of the support agent: name, source, provider, model and the get_order_status tool',
    region: { left: 544, top: 386, width: 1040, height: 580 },
    widths: [520, 1040],
    fallback: true,
  },
  {
    name: 'console-workflows',
    source: 'workflows',
    frames: 'the workflows table: five rows with name, description and pattern',
    region: { left: 544, top: 446, width: 1040, height: 546 },
    widths: [520, 1040],
    fallback: true,
  },
  {
    name: 'console-jobs',
    source: 'jobs',
    frames: 'the nightly-summary schedule and the two recent jobs with handler key and target',
    region: { left: 544, top: 322, width: 1000, height: 640 },
    widths: [500, 1000],
    fallback: true,
  },
];

export const THEMES = ['', '-light'];

/** Every file the crops produce, by published name (`approvals-wide-light-1112.avif`). */
export function landingFiles() {
  return CROPS.flatMap((crop) =>
    THEMES.flatMap((theme) =>
      crop.widths.flatMap((width, index) => {
        const stem = `${crop.name}${theme}-${width}`;
        return crop.fallback && index === 0 ? [`${stem}.avif`, `${stem}.webp`] : [`${stem}.avif`];
      }),
    ),
  );
}

/** Errors for every source screenshot that no longer matches its pin. */
export function capturePinErrors(screenshotsDir) {
  const errors = [];
  for (const [file, pin] of Object.entries(CAPTURE_PINS)) {
    const path = join(screenshotsDir, file);
    if (!existsSync(path)) {
      errors.push(`Console capture ${file} is missing; the home page crops are cut from it.`);
      continue;
    }
    const hash = createHash('sha256').update(readFileSync(path)).digest('hex');
    if (hash !== pin) {
      const crops = CROPS.filter((crop) => file.startsWith(`${crop.source}.`) || file.startsWith(`${crop.source}-light.`));
      errors.push(
        `Console capture ${file} changed (sha256 ${hash}). Put that hash in CAPTURE_PINS in ` +
          `docs-site/scripts/landing-captures.mjs, run node scripts/build-landing-crops.mjs, and open ` +
          `public/landing/${crops.map((crop) => `${crop.name}*`).join(', ')} to confirm they still frame: ` +
          `${crops.map((crop) => crop.frames).join('; ')}.`,
      );
    }
  }
  return errors;
}

/**
 * The content gate's check: the pins hold, and the home page references exactly
 * the files the crops produce. `srcset` candidates are included, which the link
 * gate does not read.
 */
export function checkLandingCaptures(siteRoot, docsRoot) {
  const errors = capturePinErrors(join(siteRoot, 'public/screenshots'));
  const landing = readFileSync(join(docsRoot, 'index.mdx'), 'utf8');
  const referenced = new Set([...landing.matchAll(/(?<=["\s])\/landing\/([\w.-]+)/g)].map((match) => match[1]));
  const produced = new Set(landingFiles());

  for (const file of referenced) {
    if (!produced.has(file)) errors.push(`index.mdx references /landing/${file}, which no crop produces.`);
  }
  for (const file of produced) {
    if (!referenced.has(file)) errors.push(`The crop file /landing/${file} is produced but index.mdx never uses it.`);
  }
  return errors;
}
