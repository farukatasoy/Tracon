// Cuts the home page's console captures out of the E2E screenshots.
//
// The screenshots in public/screenshots/ come from the end-to-end run and stay the
// one source of every console image on the site. The home page shows two small
// regions of them — the approval row for cancel_order and a failed tool call with
// its arguments — at a size where the console text can be read. A full 2880x1800
// PNG scaled into a column made that text 2–5 px tall and cost the reader
// 1.2–1.4 MB; the crops cost 8–26 KB per visit.
//
// The output goes to public/landing/, which is ignored by git and rebuilt on every
// `npm run generate` and `generate:fast`, so the crops can never drift from their
// source. What each crop frames, and the screenshot it was framed on, is declared
// in landing-captures.mjs; every pin is checked before anything is removed, so a
// failed run leaves the previous crops in place.
//
// Run: node scripts/build-landing-crops.mjs

import { mkdirSync, readFileSync, rmSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';

import { CROPS, THEMES, capturePinErrors, landingFiles } from './landing-captures.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const sources = resolve(here, '../public/screenshots');
const output = resolve(here, '../public/landing');

const pinErrors = capturePinErrors(sources);
if (pinErrors.length > 0) throw new Error(pinErrors.join('\n'));

rmSync(output, { recursive: true, force: true });
mkdirSync(output, { recursive: true });

const written = [];
let bytes = 0;

for (const crop of CROPS) {
  for (const theme of THEMES) {
    const file = `${crop.source}${theme}.png`;
    const input = readFileSync(resolve(sources, file));

    const { width, height } = await sharp(input).metadata();
    if (width !== 2880 || height !== 1800) {
      throw new Error(`Console capture ${file} is ${width}x${height}; the crop regions assume 2880x1800.`);
    }

    for (const [index, target] of crop.widths.entries()) {
      const base = sharp(input).extract(crop.region).resize({ width: target });
      const stem = `${crop.name}${theme}-${target}`;
      const avif = await base.clone().avif({ quality: 55, effort: 6 }).toFile(resolve(output, `${stem}.avif`));
      written.push(`${stem}.avif`);
      bytes += avif.size;

      if (crop.fallback && index === 0) {
        const webp = await base.clone().webp({ quality: 78 }).toFile(resolve(output, `${stem}.webp`));
        written.push(`${stem}.webp`);
        bytes += webp.size;
      }
    }
  }
}

// The content gate compares the page against landingFiles(); this keeps that
// list and the writer above from drifting apart.
if (written.sort().join() !== landingFiles().sort().join()) {
  throw new Error('build-landing-crops.mjs wrote a different file set than landingFiles() declares.');
}

console.log(`Landing crops: ${written.length} files, ${bytes} B, in public/landing/.`);
