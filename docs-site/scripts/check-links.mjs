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
const anchorsByFile = new Map();
let checked = 0;

for (const page of pages) {
  const html = readFileSync(page, 'utf8');

  for (const match of html.matchAll(/(?:href|src)="([^"]+)"/g)) {
    const target = match[1];

    if (!target.startsWith(base) && !target.startsWith('#')) {
      continue; // external or protocol-relative
    }

    checked += 1;

    const resolved = resolveTarget(target, page);
    if (!resolved.file) {
      broken.push(`${page.slice(dist.length) || '/'} → ${target}`);
      continue;
    }

    if (resolved.fragment && !hasAnchor(resolved.file, resolved.fragment)) {
      broken.push(`${page.slice(dist.length) || '/'} → ${target} (missing anchor)`);
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

/** A URL maps to a concrete HTML or asset file and an optional anchor. */
function resolveTarget(url, currentPage) {
  const [pathAndQuery, rawFragment] = url.split('#', 2);
  const fragment = safeDecode(rawFragment ?? '');

  if (url.startsWith('#')) {
    return { file: currentPage, fragment };
  }

  const path = safeDecode(pathAndQuery.slice(base.length).split('?')[0]);

  if (path === '') {
    const file = join(dist, 'index.html');
    return { file: existsSync(file) ? file : null, fragment };
  }

  const candidate = join(dist, path);

  if (existsSync(candidate)) {
    if (statSync(candidate).isDirectory()) {
      const index = join(candidate, 'index.html');
      return { file: existsSync(index) ? index : null, fragment };
    }

    return { file: candidate, fragment };
  }

  if (existsSync(`${candidate}.html`)) {
    return { file: `${candidate}.html`, fragment };
  }

  const index = join(candidate, 'index.html');
  return { file: existsSync(index) ? index : null, fragment };
}

function hasAnchor(file, fragment) {
  if (!file.endsWith('.html')) {
    return false;
  }

  if (!anchorsByFile.has(file)) {
    const html = readFileSync(file, 'utf8');
    anchorsByFile.set(
      file,
      new Set(
        [...html.matchAll(/\s(?:id|name)="([^"]+)"/g)].map((match) => decodeHtml(match[1])),
      ),
    );
  }

  return anchorsByFile.get(file).has(fragment);
}

function safeDecode(value) {
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

function decodeHtml(value) {
  return value.replaceAll('&amp;', '&').replaceAll('&quot;', '"').replaceAll('&#39;', "'");
}

function collect(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) =>
    entry.isDirectory() ? collect(join(directory, entry.name)) : [join(directory, entry.name)],
  );
}
