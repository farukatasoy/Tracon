// Renders every raster derivative of the mark: the NuGet package icon at the repo
// root, and the site's .ico and apple-touch icons. assets/tracon-mark.svg is the one
// source; nothing here draws.
//
// This is NOT part of `npm run build`. nuget.org rejects SVG but the icon changes only
// when the brand changes, same rationale as build-social-images.mjs: a committed PNG
// beats a build-time render step for an asset this static.
//
// Run: node scripts/build-package-icon.mjs

import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import sharp from 'sharp';

// 128x128 matches nuget.org's own recommendation and stays far under its 1 MB limit.
const SIZE = 128;

const here = dirname(fileURLToPath(import.meta.url));
const source = resolve(here, '../../assets/tracon-mark.svg');
const target = resolve(here, '../../assets/icon.png');

const svg = readFileSync(source);
const png = await sharp(svg).resize(SIZE, SIZE).png({ compressionLevel: 9 }).toBuffer();

mkdirSync(dirname(target), { recursive: true });
writeFileSync(target, png);
console.log(`assets/icon.png  ${png.length} B (${SIZE}x${SIZE}, from ${source})`);

// Commit static derivatives so neither the console nor package build needs the site.
for (const destination of ['../public/favicon.svg', '../../src/Tracon.UI/frontend/src/assets/tracon-mark.svg']) {
  const path = resolve(here, destination);
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, svg);
}

// 🚨 An SVG favicon alone is not enough. A browser that does not take it asks for
// /favicon.ico BY NAME, and the site answered 404 — so the tab kept whatever icon it
// had cached, which for a reader who visited before the rename is the previous brand.
// iOS ignores both and reads apple-touch-icon.png for the home screen.
//
// The ICO container holds PNG frames directly, which every browser has read since
// Windows Vista. Layout: a 6-byte directory header, one 16-byte entry per frame, then
// the frames themselves.
function encodeIco(frames) {
  const header = Buffer.alloc(6);
  header.writeUInt16LE(0, 0); // reserved
  header.writeUInt16LE(1, 2); // 1 = icon
  header.writeUInt16LE(frames.length, 4);

  const directory = Buffer.alloc(16 * frames.length);
  let offset = header.length + directory.length;

  frames.forEach(({ size, data }, index) => {
    const entry = index * 16;
    // A 256 px frame is written as 0; nothing here is that large, but the rule is the
    // format's, not ours.
    directory.writeUInt8(size >= 256 ? 0 : size, entry);
    directory.writeUInt8(size >= 256 ? 0 : size, entry + 1);
    directory.writeUInt8(0, entry + 2); // palette size: 0 for a PNG frame
    directory.writeUInt8(0, entry + 3); // reserved
    directory.writeUInt16LE(1, entry + 4); // colour planes
    directory.writeUInt16LE(32, entry + 6); // bits per pixel
    directory.writeUInt32LE(data.length, entry + 8);
    directory.writeUInt32LE(offset, entry + 12);
    offset += data.length;
  });

  return Buffer.concat([header, directory, ...frames.map((frame) => frame.data)]);
}

const render = async (size) => sharp(svg).resize(size, size).png({ compressionLevel: 9 }).toBuffer();

const icoFrames = await Promise.all(
  [16, 32, 48].map(async (size) => ({ size, data: await render(size) })),
);
const ico = encodeIco(icoFrames);
writeFileSync(resolve(here, '../public/favicon.ico'), ico);
console.log(`docs-site/public/favicon.ico  ${ico.length} B (${icoFrames.map((f) => f.size).join('/')})`);

// 180x180 is the size iOS asks for; smaller ones are upscaled on the home screen.
const touch = await render(180);
writeFileSync(resolve(here, '../public/apple-touch-icon.png'), touch);
console.log(`docs-site/public/apple-touch-icon.png  ${touch.length} B (180x180)`);
