// Post-processes the Vite output so the .NET package can embed it.
//
// Three jobs:
//   1. Insert the `<base href>` placeholder the host rewrites at run time.
//   2. Brotli-compress text assets in place, so the assembly carries the small
//      form and a client that accepts `br` is served with zero CPU cost.
//   3. Enforce the JavaScript bundle budget.
//
// It runs as part of `npm run build`, which means the budget is a real gate
// locally and in CI, not a CI-only afterthought that fails after the fact.

import { createHash } from 'node:crypto';
import { brotliCompressSync, constants, gzipSync } from 'node:zlib';
import { readFileSync, readdirSync, statSync, unlinkSync, writeFileSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';

const OUT_DIR = resolve(import.meta.dirname, '..', '..', 'wwwroot');
const SHELL = join(OUT_DIR, 'index.html');
const BASE_PLACEHOLDER = '__AGENTPRISM_BASE__';

/** Extensions worth compressing. Images and fonts are already compressed. */
const COMPRESSIBLE = new Set(['.js', '.mjs', '.css', '.svg', '.json', '.map', '.txt', '.webmanifest']);

/** gzip budget for JavaScript, in bytes. Exceeding it fails the build. */
const JS_BUDGET_BYTES = 250 * 1024;

function walk(directory) {
  const files = [];

  for (const entry of readdirSync(directory)) {
    const path = join(directory, entry);

    if (statSync(path).isDirectory()) {
      files.push(...walk(path));
    } else {
      files.push(path);
    }
  }

  return files;
}

function extensionOf(path) {
  const index = path.lastIndexOf('.');

  return index === -1 ? '' : path.slice(index);
}

/**
 * Writes the base placeholder into the shell.
 *
 * Vite is deliberately not asked to emit the tag. Its HTML pipeline rewrites
 * `href` attributes it recognises, and a `<base>` tag it decided to resolve
 * would silently break every asset URL under a non-root prefix.
 */
function prepareShell() {
  const html = readFileSync(SHELL, 'utf8');

  if (html.includes(BASE_PLACEHOLDER)) {
    return;
  }

  const head = html.indexOf('<head>');

  if (head === -1) {
    throw new Error('index.html has no <head> element; cannot insert the base tag.');
  }

  const insertAt = head + '<head>'.length;
  const tag = `\n    <base href="${BASE_PLACEHOLDER}" />`;

  writeFileSync(SHELL, html.slice(0, insertAt) + tag + html.slice(insertAt));
}

function compress(files) {
  let before = 0;
  let after = 0;

  for (const file of files) {
    if (!COMPRESSIBLE.has(extensionOf(file))) {
      continue;
    }

    const raw = readFileSync(file);
    const compressed = brotliCompressSync(raw, {
      params: {
        [constants.BROTLI_PARAM_QUALITY]: constants.BROTLI_MAX_QUALITY,
        [constants.BROTLI_PARAM_SIZE_HINT]: raw.length,
      },
    });

    // Only keep the compressed form when it actually helps. A tiny file can
    // grow, and storing a larger payload to save nothing is pure loss.
    if (compressed.length >= raw.length) {
      before += raw.length;
      after += raw.length;

      continue;
    }

    writeFileSync(`${file}.br`, compressed);
    unlinkSync(file);

    before += raw.length;
    after += compressed.length;
  }

  return { before, after };
}

function enforceBudget(files) {
  let gzipped = 0;

  for (const file of files) {
    const extension = extensionOf(file);

    if (extension !== '.js' && extension !== '.mjs') {
      continue;
    }

    gzipped += gzipSync(readFileSync(file), { level: 9 }).length;
  }

  const kilobytes = (gzipped / 1024).toFixed(1);
  const budget = (JS_BUDGET_BYTES / 1024).toFixed(0);

  if (gzipped > JS_BUDGET_BYTES) {
    throw new Error(
      `JavaScript bundle is ${kilobytes} KB gzipped, over the ${budget} KB budget. ` +
        'Remove a dependency or split the code; the budget is a gate, not a target.',
    );
  }

  return { gzipped, kilobytes, budget };
}

function main() {
  prepareShell();

  // The budget is measured before compression rewrites the files.
  const files = walk(OUT_DIR);
  const budget = enforceBudget(files);
  const sizes = compress(walk(OUT_DIR));

  const digest = createHash('sha256');

  for (const file of walk(OUT_DIR).sort()) {
    digest.update(relative(OUT_DIR, file).replaceAll('\\', '/'));
    digest.update(readFileSync(file));
  }

  process.stdout.write(
    [
      `AgentPrism UI assets`,
      `  javascript : ${budget.kilobytes} KB gzipped (budget ${budget.budget} KB)`,
      `  embedded   : ${(sizes.after / 1024).toFixed(1)} KB brotli, from ${(sizes.before / 1024).toFixed(1)} KB`,
      `  digest     : ${digest.digest('hex').slice(0, 16)}`,
      '',
    ].join('\n'),
  );
}

main();
