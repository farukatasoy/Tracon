// Renders the four link-preview images once, into public/social/.
//
// This is NOT part of `npm run build`. The images change when the brand changes,
// which is roughly never, so they are committed like any other asset and this
// script exists to reproduce them rather than to run on every publish. Rendering
// per page at build time was rejected: it would add a generated-file pipeline for
// an asset that four files already cover.
//
// Run: node scripts/build-social-images.mjs

import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, resolve } from 'node:path';
import sharp from 'sharp';

import { siteUrl } from '../site.config.mjs';

// The address as a reader would type it: no scheme, no trailing slash. Derived
// rather than typed, because a card is a PNG — a stale address inside one is
// invisible to every text search over the repository.
const wordmark = siteUrl.replace(/^https?:\/\//, '').replace(/\/$/, '');

const here = dirname(fileURLToPath(import.meta.url));
const outputDirectory = resolve(here, '../public/social');
const styleSheet = readFileSync(resolve(here, '../src/styles/site.css'), 'utf8');

// The spectrum is read from site.css rather than repeated here. astro.config.mjs
// makes the same choice for the diagram palette, and for the same reason: a second
// copy would go stale the first time the brand moves, and nothing would say so.
function spectrumStops() {
  const declaration = /--ap-spectrum:\s*([^;]+);/.exec(styleSheet);
  if (!declaration) throw new Error('site.css does not declare --ap-spectrum');

  const stops = declaration[1].match(/#[0-9a-fA-F]{3,8}/g) ?? [];
  if (stops.length < 2) throw new Error('--ap-spectrum has fewer than two colour stops');

  return stops;
}

const ink = {
  background: '#12151b',
  panel: '#1a1f28',
  line: '#2a313d',
  text: '#f2f5fa',
  muted: '#949cab',
  mark: '#f4f6fb',
};

const spectrum = spectrumStops();

const font =
  "-apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, 'Liberation Sans', sans-serif";

// The mark from public/favicon.svg, redrawn for this canvas rather than embedded.
// The favicon is a 32px tab icon: it carries a rounded dark plate and a heavier
// stroke so it survives being tiny. On a 1200x630 card the plate would be a box
// around nothing and the stroke would read as bold, so the same geometry is drawn
// without the plate. The spectrum both share comes from site.css above.
function prismMark(x, y, size) {
  const unit = size / 32;
  return `
    <g transform="translate(${x} ${y}) scale(${unit})">
      <path d="M16 3 29 26H3Z" fill="none" stroke="${ink.mark}" stroke-width="2.2"
            stroke-linejoin="round" />
      <rect x="5.5" y="28.4" width="21" height="2.6" rx="1.3" fill="url(#spectrum)" />
    </g>`;
}

const motifs = {
  // A prism at rest: what the product is before it is any particular screen.
  overview: () => `
    <g opacity="0.2" transform="translate(936 215)">
      <path d="M92 0 184 160H0Z" fill="none" stroke="${ink.mark}" stroke-width="5"
            stroke-linejoin="round" />
      <rect x="18" y="176" width="148" height="9" rx="4.5" fill="url(#spectrum)" />
    </g>`,
  // Panels: the console is a set of screens.
  console: () => {
    const cells = [];
    for (let row = 0; row < 3; row += 1) {
      for (let column = 0; column < 3; column += 1) {
        cells.push(
          `<rect x="${column * 86}" y="${row * 76}" width="70" height="60" rx="8" ` +
            `fill="${ink.panel}" stroke="${ink.line}" stroke-width="2" />`,
        );
      }
    }
    return `<g opacity="0.85" transform="translate(936 216)">${cells.join('')}</g>`;
  },
  // A run reads as a timeline of spans, which is what the operator opens.
  operate: () => {
    const widths = [200, 156, 120, 172, 96];
    return `<g opacity="0.85" transform="translate(936 218)">${widths
      .map(
        (width, index) =>
          `<rect x="${index % 2 === 0 ? 0 : 24}" y="${index * 42}" width="${width}" height="20" ` +
          `rx="10" fill="${spectrum[index]}" />`,
      )
      .join('')}</g>`;
  },
  // Rows and columns: a reference page is a table before it is prose.
  reference: () => {
    const rows = [];
    for (let row = 0; row < 6; row += 1) {
      rows.push(
        `<rect x="0" y="${row * 38}" width="212" height="22" rx="6" fill="${ink.panel}" />`,
        `<rect x="0" y="${row * 38}" width="${row === 0 ? 212 : 84}" height="22" rx="6" ` +
          `fill="${row === 0 ? '#3b7dd8' : ink.line}" opacity="${row === 0 ? 0.55 : 1}" />`,
      );
    }
    return `<g opacity="0.9" transform="translate(936 222)">${rows.join('')}</g>`;
  },
};

const cards = [
  {
    name: 'overview',
    title: 'Agent control plane for .NET',
    subtitle: 'Build on Microsoft Agent Framework, then operate what you built.',
  },
  {
    name: 'console',
    title: 'The embedded console',
    subtitle: 'Agents, runs, playground, evals — reading the same API you automate.',
  },
  {
    name: 'operate',
    title: 'Operate in production',
    subtitle: 'Durable runs, reconciliation, quotas, telemetry, tenants, retention.',
  },
  {
    name: 'reference',
    title: 'Reference',
    subtitle: 'Configuration, HTTP operations, compatibility, and the full API surface.',
  },
];

function card({ name, title, subtitle }) {
  const stops = spectrum
    .map((color, index) => `<stop offset="${(index / (spectrum.length - 1)) * 100}%" stop-color="${color}" />`)
    .join('');

  return `<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="630" viewBox="0 0 1200 630">
  <defs>
    <linearGradient id="spectrum" x1="0" y1="0" x2="1" y2="0">${stops}</linearGradient>
  </defs>
  <rect width="1200" height="630" fill="${ink.background}" />
  <rect width="1200" height="8" fill="url(#spectrum)" />
  ${motifs[name]()}
  ${prismMark(80, 92, 64)}
  <text x="164" y="146" font-family="${font}" font-size="34" font-weight="600"
        letter-spacing="-0.5" fill="${ink.text}">AgentPrism</text>
  <text x="80" y="332" font-family="${font}" font-size="54" font-weight="700"
        letter-spacing="-1.6" fill="${ink.text}">${title}</text>
  <text x="80" y="390" font-family="${font}" font-size="25" font-weight="400"
        fill="${ink.muted}">${subtitle}</text>
  <rect x="80" y="470" width="120" height="4" rx="2" fill="url(#spectrum)" />
  <text x="80" y="546" font-family="${font}" font-size="24" font-weight="500"
        fill="${ink.muted}">${wordmark}</text>
</svg>`;
}

mkdirSync(outputDirectory, { recursive: true });

for (const definition of cards) {
  const png = await sharp(Buffer.from(card(definition))).png({ compressionLevel: 9 }).toBuffer();
  const target = join(outputDirectory, `${definition.name}.png`);
  writeFileSync(target, png);
  console.log(`${definition.name}.png  ${png.length} B`);
}
