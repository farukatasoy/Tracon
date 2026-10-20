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
  'approvals.png': 'cc74bb0dbccd0b6698ff6fd4ea956d5d7738c972c58d677d07034ff5a3e78b05',
  'approvals-light.png': '94416063619ecc214aedfcb178cbbda54d9be43b6812916000bb49abef491b31',
  'run-detail.png': '79b608cb14ff7ecb5a7c456330e902ed7d85863c40fcbe5aec5b13bf188fedcb',
  'run-detail-light.png': '44d392eae76f8482bdc8323c7f290768f11ddb02899c3c9b86bd6c11585b4867',
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
