// Post-processes the embeddable chat widget's build output.
//
// Deliberately a SEPARATE script from postbuild.mjs, not a shared code path
// with a directory argument: the widget has its OWN, much smaller budget
// (30 KB vs the console's 250 KB), and keeping the two independent means a
// change to one gate cannot silently affect the other.

import { brotliCompressSync, constants, gzipSync } from 'node:zlib';
import { readFileSync, readdirSync, statSync, unlinkSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

const OUT_DIR = resolve(import.meta.dirname, '..', '..', 'wwwroot', 'embed');

/** gzip budget for the widget, in bytes. Exceeding it fails the build. */
const JS_BUDGET_BYTES = 30 * 1024;

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

function enforceBudget(files) {
  let gzipped = 0;

  for (const file of files) {
    if (!file.endsWith('.js') && !file.endsWith('.mjs')) {
      continue;
    }

    gzipped += gzipSync(readFileSync(file), { level: 9 }).length;
  }

  const kilobytes = (gzipped / 1024).toFixed(1);
  const budget = (JS_BUDGET_BYTES / 1024).toFixed(0);

  if (gzipped > JS_BUDGET_BYTES) {
    throw new Error(
      `Embeddable widget is ${kilobytes} KB gzipped, over the ${budget} KB budget. ` +
        'The control plane console must not leak into this bundle; check for an accidental import.',
    );
  }

  return { gzipped, kilobytes, budget };
}

/** Brotli-compresses the widget file in place, same as the console's assets. */
function compress(files) {
  for (const file of files) {
    if (!file.endsWith('.js') && !file.endsWith('.mjs')) {
      continue;
    }

    const raw = readFileSync(file);
    const compressed = brotliCompressSync(raw, {
      params: {
        [constants.BROTLI_PARAM_QUALITY]: constants.BROTLI_MAX_QUALITY,
        [constants.BROTLI_PARAM_SIZE_HINT]: raw.length,
      },
    });

    if (compressed.length >= raw.length) {
      continue;
    }

    writeFileSync(`${file}.br`, compressed);
    unlinkSync(file);
  }
}

function main() {
  const files = walk(OUT_DIR);
  const budget = enforceBudget(files);

  compress(files);

  process.stdout.write(
    [`AgentPrism embeddable widget`, `  javascript : ${budget.kilobytes} KB gzipped (budget ${budget.budget} KB)`, ''].join(
      '\n',
    ),
  );
}

main();
