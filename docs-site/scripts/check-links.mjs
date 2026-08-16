// Verifies that every internal link and image in the built site resolves to a file.
//
// The site is generated from three sources — hand-written pages, DocFX output, and
// the OpenAPI document — and the generators build links from uids and tag names. A
// rename in the C# source or a retagged endpoint silently breaks a link here, and a
// broken link in an API reference is worse than a missing one: it looks like the page
// exists.
//
// Run after `astro build`:  node scripts/check-links.mjs

import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const dist = resolve(here, '../dist');

/** Matches astro.config.mjs. Links are absolute and carry this prefix. */
const base = '/AgentPrism/';

if (!existsSync(dist)) {
  throw new Error(`${dist} not found. Run "astro build" first.`);
}

const pages = collect(dist).filter((file) => file.endsWith('.html'));
const broken = [];
let checked = 0;

for (const page of pages) {
  const html = readFileSync(page, 'utf8');

  for (const match of html.matchAll(/(?:href|src)="([^"]+)"/g)) {
    const target = match[1];

    if (!target.startsWith(base)) {
      continue; // external, anchor-only, or protocol-relative
    }

    checked += 1;

    if (!resolves(target)) {
      broken.push(`${page.slice(dist.length) || '/'} → ${target}`);
    }
  }
}

if (broken.length > 0) {
  console.error(`${broken.length} broken internal link(s):`);
  for (const entry of broken.slice(0, 50)) {
    console.error(`  ${entry}`);
  }

  process.exit(1);
}

console.log(`Links: ${checked} internal reference(s) across ${pages.length} pages, none broken.`);

/** A URL under the base maps to a file, or to the index.html of a directory. */
function resolves(url) {
  const path = decodeURIComponent(url.slice(base.length).split('#')[0].split('?')[0]);

  if (path === '') {
    return existsSync(join(dist, 'index.html'));
  }

  const candidate = join(dist, path);

  if (existsSync(candidate)) {
    return statSync(candidate).isDirectory() ? existsSync(join(candidate, 'index.html')) : true;
  }

  return existsSync(`${candidate}.html`) || existsSync(join(candidate, 'index.html'));
}

function collect(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) =>
    entry.isDirectory() ? collect(join(directory, entry.name)) : [join(directory, entry.name)],
  );
}
