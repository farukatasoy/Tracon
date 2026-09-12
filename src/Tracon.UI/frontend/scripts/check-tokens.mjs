import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

/**
 * The contrast gate for the console's token set.
 *
 * Two failures this catches that reading the file cannot:
 *
 *  1. A token defined in only ONE theme. It looks right in whichever theme the
 *     author had open and resolves to nothing in the other — the text keeps the
 *     inherited colour and silently loses its contrast.
 *  2. A pair that drifts below AA. Every value here is measured, never judged
 *     by eye; the floor recorded below only ever moves up.
 *
 * Phase 163 measured the same two floors on the documentation site (5.49:1 text,
 * 3.74:1 non-text) and wrote them into the consumer-documentation quality
 * contract. This file is the console's half of that measurement.
 */

// A plain Node script rather than a Vitest case: reading a file needs
// `node:fs`, and the console ships no `@types/node` on purpose, so a test that
// imported it would fail `tsc --noEmit`. `npm run build` runs this first.
const root = resolve(import.meta.dirname, '..');
const css = readFileSync(resolve(root, 'src', 'styles.css'), 'utf8');
const shell = readFileSync(resolve(root, 'index.html'), 'utf8');

/** WCAG 2.1 minimum for normal-size text. */
const TEXT_MINIMUM = 4.5;

/** WCAG 2.1 minimum for a UI component boundary or a graphical object. */
const NON_TEXT_MINIMUM = 3;

function block(selector) {
  const start = css.indexOf(`${selector} {`);

  if (start === -1) {
    throw new Error(`styles.css has no '${selector}' block.`);
  }

  const end = css.indexOf('\n}', start);
  const body = css.slice(start, end);
  const tokens = {};

  for (const [, name, value] of body.matchAll(/(--tracon-[a-z0-9-]+):\s*([^;]+);/g)) {
    tokens[name] = value.trim();
  }

  return tokens;
}

const dark = block(':root');
const light = block(":root[data-theme='light']");

function channel(value) {
  const c = value / 255;

  return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
}

function luminance(hex) {
  const parsed = /^#([0-9a-f]{6})$/i.exec(hex);

  if (parsed === null) {
    throw new Error(`Not a six-digit hex colour: ${hex}`);
  }

  const packed = Number.parseInt(parsed[1], 16);
  const r = channel((packed >> 16) & 0xff);
  const g = channel((packed >> 8) & 0xff);
  const b = channel(packed & 0xff);

  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrast(foreground, background) {
  const a = luminance(foreground);
  const b = luminance(background);
  const [high, low] = a > b ? [a, b] : [b, a];

  return (high + 0.05) / (low + 0.05);
}

/** Surfaces a given piece of text or boundary can land on. */
const SURFACES = ['bg', 'panel', 'raised'];

/** Text tokens that must clear AA on every surface they are used over. */
const TEXT_ON_SURFACES = ['fg', 'muted', 'subtle', 'accent'];

/** Status text sits inside its own soft chip AND bare on a panel. */
const STATUS = ['success', 'warn', 'danger', 'info'];

/** Series colours are graphical objects, not text. */
const SERIES = ['series-1', 'series-2', 'series-3', 'series-4', 'series-5', 'series-6'];

function measure(theme, tokens) {
  const value = (name) => {
    const found = tokens[`--tracon-${name}`];

    if (found === undefined) {
      throw new Error(`${theme} theme does not define --tracon-${name}.`);
    }

    return found;
  };

  const measurements = [];

  const add = (pair, foreground, background, minimum) => {
    measurements.push({ theme, pair, ratio: contrast(foreground, background), minimum });
  };

  for (const token of TEXT_ON_SURFACES) {
    for (const surface of SURFACES) {
      add(`${token} on ${surface}`, value(token), value(surface), TEXT_MINIMUM);
    }
  }

  for (const token of STATUS) {
    add(`${token} on ${token}-soft`, value(token), value(`${token}-soft`), TEXT_MINIMUM);
    add(`${token} on panel`, value(token), value('panel'), TEXT_MINIMUM);
    add(`${token} on raised`, value(token), value('raised'), TEXT_MINIMUM);
  }

  add('accent on accent-soft', value('accent'), value('accent-soft'), TEXT_MINIMUM);

  // The accent button paints `accent-fg` on a solid `accent` field.
  add('accent-fg on accent', value('accent-fg'), value('accent'), TEXT_MINIMUM);

  // A form control's boundary has to be findable, and the focus ring has to be
  // visible against whatever it surrounds.
  for (const surface of SURFACES) {
    add(`line-strong on ${surface}`, value('line-strong'), value(surface), NON_TEXT_MINIMUM);
  }

  for (const token of SERIES) {
    add(`${token} on panel`, value(token), value('panel'), NON_TEXT_MINIMUM);
  }

  return measurements;
}

function main() {
  const failures = [];

  const darkNames = Object.keys(dark).sort();
  const lightNames = Object.keys(light).sort();

  if (darkNames.join() !== lightNames.join()) {
    const onlyDark = darkNames.filter((name) => !(name in light));
    const onlyLight = lightNames.filter((name) => !(name in dark));

    failures.push(
      `Token names differ between themes. Dark only: [${onlyDark.join(', ')}]. Light only: [${onlyLight.join(', ')}].`,
    );
  }

  // A token pointing at another token hides a missing definition: the fallback
  // chain still resolves, just to the wrong colour.
  for (const [name, value] of [...Object.entries(dark), ...Object.entries(light)]) {
    if (value.includes('var(--tracon-')) {
      failures.push(`${name} is defined as a reference to another token, not a literal value.`);
    }
  }

  // 🚨 A colour defined inside a @media query or a component rule is invisible
  // to this gate and disappears when the theme flips. Phase 163 measured this
  // on the site; the same rule holds here.
  const declarations = [...css.matchAll(/(--tracon-[a-z0-9-]+):/g)].length;
  const expected = darkNames.length + lightNames.length;

  if (declarations !== expected) {
    failures.push(
      `styles.css declares ${declarations} --tracon-* tokens but the two theme blocks hold ${expected}. ` +
        'A token defined outside :root or :root[data-theme=\'light\'] is invisible in the other theme.',
    );
  }

  // 🚨 `index.html` paints the ground colour before the stylesheet is fetched,
  // so it carries a COPY of `--tracon-bg` for each theme. A copy with no gate
  // drifts: the token moves, the cold-load flash comes back, and nothing says
  // so. The shell is the only place a token value may be duplicated.
  const ground = Object.fromEntries(
    [...shell.matchAll(/html(\[data-theme='(light)'\])?\s*\{\s*background-color:\s*(#[0-9a-f]{6});/g)].map(
      (match) => [match[2] ?? 'dark', match[3]],
    ),
  );

  for (const [theme, tokens] of [['dark', dark], ['light', light]]) {
    const expected = tokens['--tracon-bg'];

    if (ground[theme] !== expected) {
      failures.push(
        `index.html paints ${ground[theme] ?? '(nothing)'} as the ${theme} ground, ` +
          `but --tracon-bg is ${expected}. The cold-load ground must match the token.`,
      );
    }
  }

  const measurements = [...measure('dark', dark), ...measure('light', light)];

  for (const entry of measurements.filter((item) => item.ratio < item.minimum)) {
    failures.push(
      `${entry.theme}: ${entry.pair} is ${entry.ratio.toFixed(2)}:1, below the ${entry.minimum}:1 minimum.`,
    );
  }

  const floorOf = (minimum) => {
    const scoped = measurements.filter((entry) => entry.minimum === minimum);
    return scoped.reduce((lowest, entry) => (entry.ratio < lowest.ratio ? entry : lowest), scoped[0]);
  };

  const textFloor = floorOf(TEXT_MINIMUM);
  const nonTextFloor = floorOf(NON_TEXT_MINIMUM);

  // 🚨 These two numbers are the console's entry in the consumer documentation
  // quality contract (table F). They are MEASURED, not chosen. Lowering either
  // is a regression — raise them when the palette improves, never to make a
  // change fit. They sit a hair below the measured 5.22 / 3.56 on purpose: a
  // floor written to the last digit turns an imperceptible rounding into a red
  // build, which teaches people to edit the floor.
  const RECORDED_TEXT_FLOOR = 5.2;
  const RECORDED_NON_TEXT_FLOOR = 3.5;

  if (textFloor.ratio < RECORDED_TEXT_FLOOR) {
    failures.push(
      `Text contrast floor fell to ${textFloor.ratio.toFixed(2)}:1 (${textFloor.theme} ${textFloor.pair}); ` +
        `the recorded floor is ${RECORDED_TEXT_FLOOR}:1.`,
    );
  }

  if (nonTextFloor.ratio < RECORDED_NON_TEXT_FLOOR) {
    failures.push(
      `Non-text contrast floor fell to ${nonTextFloor.ratio.toFixed(2)}:1 ` +
        `(${nonTextFloor.theme} ${nonTextFloor.pair}); the recorded floor is ${RECORDED_NON_TEXT_FLOOR}:1.`,
    );
  }

  if (failures.length > 0) {
    throw new Error(`Console token set:\n  ${failures.join('\n  ')}`);
  }

  process.stdout.write(
    [
      'Tracon UI tokens',
      `  tokens     : ${darkNames.length} in each theme, ${measurements.length} pairs measured`,
      `  ground     : ${ground.dark} dark / ${ground.light} light, matching index.html`,
      `  text       : ${textFloor.ratio.toFixed(2)}:1 floor (${textFloor.theme} ${textFloor.pair}), minimum ${TEXT_MINIMUM}:1`,
      `  non-text   : ${nonTextFloor.ratio.toFixed(2)}:1 floor (${nonTextFloor.theme} ${nonTextFloor.pair}), minimum ${NON_TEXT_MINIMUM}:1`,
      '',
    ].join('\n'),
  );
}

main();
