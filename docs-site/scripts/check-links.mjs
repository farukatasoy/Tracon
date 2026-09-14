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
import { dirname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

import { base } from '../site.config.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const dist = resolve(here, '../dist');

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
    let target = match[1];

    // `base` is '/', so the prefix test alone would claim protocol-relative
    // addresses (`//cdn.example/x`) as internal and report them all as broken.
    if (target.startsWith('//') || /^[a-z][a-z0-9+.-]*:/i.test(target)) {
      continue; // external or protocol-relative
    }

    // A scheme-less address that starts with neither `/` nor `#` is relative to the
    // page, not external — and treating it as external is how 640 links into a `.md`
    // file that is never published stayed invisible here. Resolve it against the page
    // and let the same checks below judge it.
    if (!target.startsWith(base) && !target.startsWith('#')) {
      const pagePath = page.slice(dist.length).split(sep).join('/');

      try {
        const resolvedUrl = new URL(target, `https://site.invalid${pagePath}`);

        target = `${resolvedUrl.pathname}${resolvedUrl.search}${resolvedUrl.hash}`;
      } catch {
        // An address this malformed cannot resolve for a reader either.
        checked += 1;
        broken.push(`${page.slice(dist.length) || '/'} → ${target}`);
        continue;
      }
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

// The page index in llms.txt is the only set of internal links that lives
// outside an HTML page, so the sweep above cannot see it. It is generated from
// the pages themselves, which means a broken line here is a slug that no longer
// resolves - and an agent that fetches this file has no site navigation to fall
// back on.
const indexFile = join(dist, 'llms.txt');

if (existsSync(indexFile)) {
  for (const match of readFileSync(indexFile, 'utf8').matchAll(/^- \[[^\]]*]\((\S+)\)/gm)) {
    checked += 1;

    const path = new URL(match[1]).pathname;

    if (!path.startsWith(base) || !resolveTarget(path, indexFile).file) {
      broken.push(`/llms.txt \u2192 ${match[1]}`);
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

console.log(`Links: ${checked} internal reference(s) across ${pages.length} pages and llms.txt, none broken.`);

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
