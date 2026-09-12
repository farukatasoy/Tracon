// Reproduce the four committed social cards from the shared mark and site tokens.
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, resolve } from 'node:path';
import sharp from 'sharp';
import { siteUrl } from '../site.config.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const outputDirectory = resolve(here, '../public/social');
const styleSheet = readFileSync(resolve(here, '../src/styles/site.css'), 'utf8');
const mark = readFileSync(resolve(here, '../../assets/tracon-mark.svg'));
const token = (name) => {
  const match = new RegExp(`--tracon-${name}:\\s*(#[0-9a-fA-F]{6})\\s*;`).exec(styleSheet);
  if (!match) throw new Error(`Missing colour token: ${name}`);
  return match[1];
};
const ink = Object.fromEntries(['surface', 'text-strong', 'text-muted', 'border', 'accent'].map(name => [name, token(name)]));
const escape = (value) => value.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('"', '&quot;');
const address = escape(siteUrl.replace(/^https?:\/\//, '').replace(/\/$/, ''));
const cards = [
  { name: 'overview', label: '.NET PACKAGE FAMILY', title: 'Your agents fly.', second: 'You own the airspace.', detail: 'The control plane on Microsoft Agent Framework.' },
  { name: 'console', label: 'EMBEDDED CONSOLE', title: 'Inspect the run.', second: 'Understand the result.', detail: 'Definitions, runs, evaluation, and configured controls.' },
  { name: 'operate', label: 'OPERATE', title: 'Make execution', second: 'inspectable.', detail: 'Recording, telemetry, persistence, and operational limits.' },
  { name: 'reference', label: 'TECHNICAL REFERENCE', title: 'Find the contract.', second: 'Build with confidence.', detail: '.NET API, HTTP operations, configuration, and compatibility.' },
];
function card({ label, title, second, detail }) {
  return `<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="630" viewBox="0 0 1200 630">
    <rect width="1200" height="630" fill="${ink.surface}"/>
    <image href="data:image/svg+xml;base64,${mark.toString('base64')}" x="64" y="52" width="64" height="64"/>
    <g font-family="Helvetica, Arial, sans-serif">
      <text x="148" y="96" font-size="32" font-weight="600" fill="${ink['text-strong']}">Tracon</text>
      <path d="M64 150H1136M64 506H1136" stroke="${ink.border}"/>
      <text x="64" y="208" font-family="monospace" font-size="19" letter-spacing="2" fill="${ink.accent}">${escape(label)}</text>
      <text x="60" y="304" font-size="68" font-weight="600" letter-spacing="-2" fill="${ink['text-strong']}">${escape(title)}</text>
      <text x="60" y="383" font-size="68" font-weight="600" letter-spacing="-2" fill="${ink.accent}">${escape(second)}</text>
      <text x="64" y="451" font-size="25" fill="${ink['text-muted']}">${escape(detail)}</text>
      <text x="64" y="566" font-size="23" fill="${ink['text-muted']}">${address}</text>
    </g>
  </svg>`;
}
mkdirSync(outputDirectory, { recursive: true });
for (const definition of cards) {
  const png = await sharp(Buffer.from(card(definition))).png({ compressionLevel: 9 }).toBuffer();
  writeFileSync(join(outputDirectory, `${definition.name}.png`), png);
  console.log(`${definition.name}.png  ${png.length} B`);
}
