import { readFileSync, readdirSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';

/**
 * The gate that keeps the console's modal layer in one place.
 *
 * Phase 175 found nine call sites reaching for `window.confirm`, in a console
 * whose `dialog.tsx` already carried a focus trap, an Esc handler and focus
 * return. A native modal has none of what those nine needed:
 *
 *  - it cannot be styled, so it arrives in the browser's chrome, in the
 *    browser's language, ignoring the console's own locale (K-232 is about the
 *    SERVER's language; this was the operating system's);
 *  - its default button is the ACCEPT one in every major browser, which is the
 *    exact mis-click §175.4 rule 1 exists to prevent;
 *  - it blocks the event loop, and Playwright auto-dismisses it, so no E2E case
 *    could click a delete button at all — which is why no E2E case did.
 *
 * So the rule is not "prefer `ConfirmDialog`", it is "there is one modal layer".
 * `window.alert` and `window.prompt` are banned for the same reasons; a message
 * belongs in `ErrorNote` and an input belongs in a form.
 *
 * A plain Node script rather than a Vitest case, for the reason recorded in
 * `check-tokens.mjs`: reading a file needs `node:fs` and the console ships no
 * `@types/node`, so an importing test would fail `tsc --noEmit`.
 */
const root = resolve(import.meta.dirname, '..');
const source = resolve(root, 'src');

/** Where focus trapping, Esc and focus return actually live. */
const MODAL_LAYER = 'components/dialog.tsx';

/**
 * The hook a full-screen overlay has to be wired to.
 *
 * An allowlist of files would have been the easy gate and the wrong one: the
 * command palette legitimately draws its own backdrop because it is not shaped
 * like a `Dialog` (it anchors to the top and is a search sheet, not a panel),
 * and it is correct precisely because it runs `useFocusTrap`. So the rule is
 * about the four properties, not about the file name.
 */
// 🚨 The CALL, not the name. Matching the bare identifier let a file satisfy
// the gate with a comment that merely mentioned the hook — an audit found the
// hole. `useFocusTrap(` can only appear where the hook is actually invoked or
// declared.
const FOCUS_TRAP = 'useFocusTrap(';

// The lookbehind is the whole trick: it rejects `foo.confirm(` and
// `setConfirming(` while still matching a bare `confirm(` and the qualified
// `window.confirm(`. Case matters — `onConfirm(` is a prop, not a native call.
const NATIVE_MODALS =
  /(?<![.\w])(?:(?:window|globalThis|self)\.)?(confirm|alert|prompt)\s*\(/g;

/**
 * The bracket spelling of the same three calls.
 *
 * `window['confirm'](…)` reads as an escape hatch and is treated as one: the
 * regex above cannot see it, and a rule with a one-line bypass is a rule
 * nobody has to follow.
 */
const BRACKET_MODALS = /\[\s*(["'`])(confirm|alert|prompt)\1\s*\]\s*\(/g;

/** A screen that draws its own full-screen panel has bypassed `Dialog`. */
const OWN_BACKDROP = /fixed\s+inset-0/;

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

function main() {
  const files = sourceFiles(source).sort();
  const failures = [];

  for (const file of files) {
    const name = relative(source, file).replaceAll('\\', '/');
    const text = readFileSync(file, 'utf8');
    const lines = text.split('\n');

    lines.forEach((line, index) => {
      const native = [
        ...[...line.matchAll(NATIVE_MODALS)].map((match) => match[1]),
        ...[...line.matchAll(BRACKET_MODALS)].map((match) => match[2]),
      ];

      for (const which of native) {
        failures.push(
          `${name}:${index + 1} calls window.${which}(). The console has one modal layer: ` +
            `use ConfirmDialog for a decision, Dialog for anything else, ErrorNote for a message.`,
        );
      }

      if (OWN_BACKDROP.test(line) && !text.includes(FOCUS_TRAP)) {
        failures.push(
          `${name}:${index + 1} draws its own 'fixed inset-0' overlay without calling ${FOCUS_TRAP}). ` +
            `Focus trapping, Esc and focus return live in ${MODAL_LAYER}; an overlay that ` +
            `does not run the hook carries none of them.`,
        );
      }
    });
  }

  if (failures.length > 0) {
    throw new Error(`Console modal layer:\n  ${failures.join('\n  ')}`);
  }

  process.stdout.write(
    [
      'Tracon UI modal layer',
      `  scanned    : ${files.length} source files under src/`,
      `  native     : 0 window.confirm / alert / prompt calls`,
      `  backdrops  : every 'fixed inset-0' overlay calls ${FOCUS_TRAP})`,
      '',
    ].join('\n'),
  );
}

main();
