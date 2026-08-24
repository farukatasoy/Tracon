// Renders the NuGet package icon once, into assets/icon.png at the repo root.
//
// This is NOT part of `npm run build`. nuget.org rejects SVG (only public/favicon.svg
// exists today) but the icon changes only when the brand changes, same rationale as
// build-social-images.mjs: a committed PNG beats a build-time render step for an asset
// this static.
//
// Run: node scripts/build-package-icon.mjs

import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import sharp from 'sharp';

// 128x128 matches nuget.org's own recommendation and stays far under its 1 MB limit.
const SIZE = 128;

const here = dirname(fileURLToPath(import.meta.url));
const source = resolve(here, '../public/favicon.svg');
const target = resolve(here, '../../assets/icon.png');

const svg = readFileSync(source);
const png = await sharp(svg).resize(SIZE, SIZE).png({ compressionLevel: 9 }).toBuffer();

mkdirSync(dirname(target), { recursive: true });
writeFileSync(target, png);
console.log(`assets/icon.png  ${png.length} B (${SIZE}x${SIZE}, from ${source})`);
