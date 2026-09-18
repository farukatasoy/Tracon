import { readFileSync, readdirSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';

/**
 * The gate that keeps a hand-written class name and its CSS rule together.
 *
 * Every other class in this console is a Tailwind utility, generated from the
 * source at build time: a misspelt one produces no rule and nothing else, so
 * the compiler, the tests and the browser all stay quiet. The console's own
 * classes are the exception - they are written by hand in `styles.css` and
 * referenced by hand in a component, with nothing tying the two together.
 *
 * Phase 162 renamed the product, and with it the `ap-` class prefix to
 * `tracon-`. One reference did not move: `transcript.tsx` kept applying
 * `ap-stream-caret` while the rule stayed `.tracon-stream-caret::after`. The
 * streaming caret was therefore never drawn, on every streaming turn, for as
 * long as the rename had been in. Nothing FAILED, which is why 79 end-to-end
 * cases went on passing.
 *
 * So the rule is symmetric: a hand-written class used in a component must have
 * a rule, and a rule must have a user. Either half alone lets the pair drift.
 *
 * A plain Node script rather than a Vitest case, for the reason recorded in
 * `check-tokens.mjs`: reading a file needs `node:fs` and the console ships no
 * `@types/node`, so an importing test would fail `tsc --noEmit`.
 */
const root = resolve(import.meta.dirname, '..');
const source = resolve(root, 'src');
const stylesheet = resolve(source, 'styles.css');

/**
 * Prefixes that mark a class as this console's own rather than Tailwind's.
 *
 * `ap-` is the retired one and is listed on purpose: a reference left behind by
 * the rename has to be reported, not silently ignored for having the old
 * prefix.
 */
const CUSTOM_PREFIXES = ['tracon-', 'ap-'];

/** Class names that a rule defines but no component may need to reference. */
const RULE_ONLY = new Set();

function sourceFiles(directory) {
  const found = [];

  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name);

    if (entry.isDirectory()) {
      found.push(...sourceFiles(path));
    } else if (/\.tsx?$/.test(entry.name) && !/\.test\.tsx?$/.test(entry.name)) {
      found.push(path);
    }
  }

  return found;
}

/** Every class name the stylesheet defines a rule for, under a custom prefix. */
function definedClasses() {
  const css = readFileSync(stylesheet, 'utf8');
  const defined = new Set();

  for (const match of css.matchAll(/\.([A-Za-z][\w-]*)/g)) {
    if (CUSTOM_PREFIXES.some((prefix) => match[1].startsWith(prefix))) {
      defined.add(match[1]);
    }
  }

  return defined;
}

/**
 * The text of every `className=` attribute in a file, with the line it starts
 * on.
 *
 * Only a className position counts. An element id is written the same way a
 * class is - `id="tracon-main"`, `list="tracon-models"` - and a scan over every
 * string literal reports all of them as classes with no rule, which is the
 * kind of noise that gets a gate switched off.
 */
function classNameRegions(text) {
  const regions = [];

  for (const match of text.matchAll(/className=/g)) {
    let index = match.index + match[0].length;
    const line = text.slice(0, index).split('\n').length;

    if (text[index] === '"' || text[index] === "'") {
      const quote = text[index];
      const end = text.indexOf(quote, index + 1);
      regions.push({ line, literal: true, text: text.slice(index + 1, end === -1 ? text.length : end) });
      continue;
    }

    if (text[index] !== '{') {
      continue;
    }

    let depth = 0;
    const start = index;

    for (; index < text.length; index++) {
      if (text[index] === '{') depth++;
      else if (text[index] === '}' && --depth === 0) break;
    }

    // A comment inside the expression names classes it does not apply - the
    // one this gate exists for is described in a comment right beside it.
    regions.push({
      line,
      literal: false,
      text: text.slice(start, index).replace(/\/\*[\s\S]*?\*\//g, ' ').replace(/\/\/[^\n]*/g, ' '),
    });
  }

  return regions;
}

/** Every custom-prefixed class name a component applies, with where it does. */
function usedClasses(files) {
  const used = new Map();

  for (const file of files) {
    const name = relative(root, file);

    for (const region of classNameRegions(readFileSync(file, 'utf8'))) {
      // A quoted attribute IS the class list; an expression carries it in the
      // string literals inside.
      const lists = region.literal
        ? [region.text]
        : [...region.text.matchAll(/'([^']*)'|"([^"]*)"|`([^`]*)`/g)]
            .map((match) => match[1] ?? match[2] ?? match[3] ?? '');

      for (const list of lists) {
        for (const token of list.split(/\s+/)) {
          if (CUSTOM_PREFIXES.some((prefix) => token.startsWith(prefix)) && /^[\w-]+$/.test(token)) {
            used.set(token, `${name}:${region.line}`);
          }
        }
      }
    }
  }

  return used;
}

function main() {
  const files = sourceFiles(source);
  const defined = definedClasses();
  const used = usedClasses(files);
  const failures = [];

  for (const [token, where] of used) {
    if (!defined.has(token)) {
      failures.push(
        `${where} applies '${token}', which styles.css defines no rule for. ` +
          `A hand-written class with no rule does nothing and reports nothing.`,
      );
    }
  }

  for (const token of defined) {
    if (!used.has(token) && !RULE_ONLY.has(token)) {
      failures.push(
        `styles.css defines '.${token}', which no component applies. ` +
          `Either a reference was renamed away from it or the rule is dead.`,
      );
    }
  }

  if (failures.length > 0) {
    throw new Error(`Console custom classes:\n  ${failures.join('\n  ')}`);
  }

  process.stdout.write(
    [
      'Tracon UI custom classes',
      `  scanned    : ${files.length} source files under src/`,
      `  matched    : ${used.size} hand-written class names, each with a rule`,
      '',
    ].join('\n'),
  );
}

main();
