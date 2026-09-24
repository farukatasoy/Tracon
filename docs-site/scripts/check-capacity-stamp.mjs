import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

/**
 * Every capacity number this site publishes must be recomputable from a stored
 * measurement artifact.
 *
 * Phase 166 measured the numbers on `guides/production.md` and its audit found
 * five defects. All five were the same class: a number that was correct in the
 * measurement file and wrong by the time it reached the page. K-775 turned that
 * into a contract - a published capacity number carries its version and its
 * commit - and this gate is the contract's enforcement.
 *
 * The gate reads only. It never rewrites the page to match the artifact,
 * because a number that drifted is a question for a person: either the page was
 * mistyped, or the measurement was never re-run.
 *
 * 🚨 K-766 says a process-measurement gate checks that a section EXISTS, not
 * that it is CORRECT, and reasons that no gate can check correctness. This gate
 * checks correctness and that is not a contradiction. A process measurement is
 * a judgement with no external source. A capacity number has a machine-readable
 * source sitting next to it. Correctness is checkable exactly when a source
 * exists; this is the other side of the same rule, not a new one.
 */

/** The page that publishes capacity numbers. */
const PAGE = 'guides/production.md';

/**
 * A published row names the measurement cell it came from, in the row itself:
 *
 *     | Buffered | 8 | 1 384 | … | 7.47 <!-- capacity: kind=latency … --> |
 *
 * The marker sits INSIDE the final cell, before the closing pipe. Two other
 * placements were measured and rejected: a comment on its own line between rows
 * ends the table at that line and destroys every row below it, and a comment
 * after the closing pipe parses as an extra cell the header does not have.
 * In-cell is also what this repository already does for behaviour claims
 * (`<!-- claim:option … -->`, phase 158).
 */
const MARKER = /<!--\s*capacity:\s*([^>]*?)\s*-->/;

/**
 * The columns each kind of capacity table publishes, in order.
 *
 * The header is compared exactly. A renamed or inserted column must come back
 * through this file, because the mapping from column to measured field is the
 * one thing a marker cannot carry.
 */
const SCHEMAS = {
  latency: ['Path', 'Concurrency', 'n', 'p50', 'p95', 'Completed/s'],
  arrival: ['Planned/s', 'Sent', 'Never sent', 'Completed', 'Queue wait p95'],
  storage: ['Path', 'Rows per run (3 repeats)', 'Bytes per run', 'Largest contributor'],
};

/**
 * How many rows of each kind the page is expected to publish.
 *
 * 🚨 Without this, deleting markers makes the gate quieter instead of louder:
 * the audit measured that removing all twelve latency markers and then
 * corrupting a number left the gate GREEN, because the remaining seven marked
 * rows kept the empty-inventory guard from firing. An inventory that the page
 * alone decides is not an inventory.
 *
 * Re-measuring the bench legitimately changes these numbers, and that is the
 * point: the change comes through this file, with the page, in one commit.
 */
const EXPECTED_ROWS = { latency: 12, arrival: 4, storage: 3 };

/** Prose numbers carry a marker too, naming the field they quote. */
const VALUE_FORMATS = {
  count: formatCount,
  p50: formatMilliseconds,
  p95: formatMilliseconds,
  p99: formatMilliseconds,
  throughputPerSecond: formatRate,
  rowsPerRun: formatRows,
};

/**
 * Below this many samples a p95 is indicative rather than a measurement.
 * `LatencyStatistics.P95SampleFloor` in the capacity driver; the two must
 * agree, so the constant is named here rather than inlined.
 */
const P95_SAMPLE_FLOOR = 100;

/** Environment fields the page states and a manifest can confirm.
 *
 * `architecture` is deliberately absent: the manifest records `arm64` and the
 * page writes "Apple Silicon", which is the same fact in the register a reader
 * uses. A gate that demanded the literal would force the page to get worse.
 *
 * 🚨 Each field renders the PHRASE the page uses, never the bare value. A bare
 * `10` for `processorCount` was satisfied by the `10` inside ".NET 10.0.100",
 * so the gate passed a page that stated 64 processors. A value that can be
 * matched by accident is not checked at all.
 */
const ENVIRONMENT_FIELDS = [
  ['operatingSystem', (value) => String(value)],
  ['processorCount', (value) => `${value} logical processors`],
  ['physicalMemoryBytes', (value) => `${Math.round(value / 1024 ** 3)} GiB`],
  ['runtimeVersion', (value) => `.NET ${value}`],
  // The manifest carries the server's full banner - "18.4 (Debian 18.4-1…)".
  // The page states the release, which is the part a reader can act on.
  ['postgreSqlVersion', (value) => `PostgreSQL ${String(value).split(' ')[0]}`],
  ['databaseImage', (value) => String(value)],
];

// ---------------------------------------------------------------------------
// Formatting. Every rule lives here exactly once.
//
// 🚨 The page and the gate must round identically or the gate invents findings
// nobody can act on, which is the worst end a gate can come to. That is why
// these are functions and not rules restated at each call site.
// ---------------------------------------------------------------------------

/** `1384` -> `1 384`. A plain space, which is what the page uses. */
function formatCount(value) {
  return String(Math.round(value)).replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
}

/** A latency, to the millisecond. */
function formatMilliseconds(value) {
  return `${Math.round(value)} ms`;
}

/**
 * A rate, to three significant figures but never more than two decimals.
 * `0.9407` -> `0.94`, `7.468` -> `7.47`, `29.4893` -> `29.5`.
 *
 * Rounding happens once. Rounding to three figures and then to two decimals
 * can land a digit away from rounding once, and the page would be right while
 * the gate called it wrong.
 */
function formatRate(value) {
  const magnitude = Math.floor(Math.log10(Math.abs(value)));
  return value.toFixed(Math.min(2, Math.max(0, 2 - magnitude)));
}

/** Storage growth, to one decimal of a KiB. */
function formatKibibytes(value) {
  return (value / 1024).toFixed(1);
}

/** Rows written per run, to one decimal. */
function formatRows(value) {
  return value.toFixed(1);
}

// ---------------------------------------------------------------------------
// Reading the page
// ---------------------------------------------------------------------------

/** `kind=latency profile=sweep` -> `{ kind: 'latency', profile: 'sweep' }`. */
function parseMarker(text) {
  const attributes = {};

  for (const pair of text.split(/\s+/).filter(Boolean)) {
    const split = pair.indexOf('=');

    if (split < 1) {
      return { error: `attribute '${pair}' is not key=value` };
    }

    attributes[pair.slice(0, split)] = pair.slice(split + 1);
  }

  return { attributes };
}

/** The cells of one table row, with any marker removed from the last one. */
function splitCells(line) {
  const trimmed = line.trim().replace(/^\|/, '').replace(/\|$/, '');
  return trimmed.split('|').map((cell) => cell.replace(MARKER, '').trim());
}

/**
 * Every maximal run of consecutive table lines in the page.
 *
 * A table is contiguous by definition in GFM - the first line that is not a row
 * ends it - so grouping by adjacency is the same grouping the renderer makes.
 */
function findTables(lines) {
  const tables = [];
  let current = null;

  lines.forEach((line, index) => {
    if (line.trimStart().startsWith('|')) {
      current ??= { start: index, lines: [] };
      current.lines.push({ line, number: index + 1 });
    } else if (current) {
      tables.push(current);
      current = null;
    }
  });

  if (current) {
    tables.push(current);
  }

  return tables;
}

// ---------------------------------------------------------------------------
// Checking one row against its measurement
// ---------------------------------------------------------------------------

/** Compare one published cell with the value recomputed from the artifact. */
function compare(errors, where, column, published, expected) {
  if (published !== expected) {
    errors.push(`${where}: ${column} publishes '${published}', measured '${expected}'`);
  }
}

/** The row's human label must name the scenario its marker cites. */
function checkPathLabel(errors, where, label, scenario) {
  if (scenario && !label.toLowerCase().includes(scenario.toLowerCase())) {
    errors.push(`${where}: row is labelled '${label}' but cites scenario '${scenario}'`);
  }
}

/** The measured cell a latency or storage marker names. */
function findCell(summary, attributes) {
  return (summary.rows ?? []).find(
    (row) =>
      row.scenario === attributes.scenario &&
      row.seedShape === attributes.seed &&
      String(row.concurrency) === attributes.concurrency,
  );
}

function checkLatencyRow(errors, where, cells, attributes, summary) {
  const cell = findCell(summary, attributes);

  if (!cell) {
    errors.push(
      `${where}: no measured cell for scenario=${attributes.scenario} ` +
        `seed=${attributes.seed} concurrency=${attributes.concurrency}`,
    );
    return;
  }

  const latency = cell.latency;
  // The label is data too: swapping "Buffered" and "Streaming" while leaving
  // the numbers and markers in place was measured to pass. A row that names
  // one path and cites another is wrong however right its numbers are.
  checkPathLabel(errors, where, cells[0], attributes.scenario);
  compare(errors, where, 'concurrency', cells[1], String(cell.concurrency));
  compare(errors, where, 'n', cells[2], formatCount(latency.count));
  compare(errors, where, 'p50', cells[3], formatMilliseconds(latency.p50));
  compare(errors, where, 'p95', cells[4], formatMilliseconds(latency.p95));
  compare(errors, where, 'Completed/s', cells[5], formatRate(cell.throughputPerSecond));

  // 🚨 Phase 166's third finding: a p95 whose merged count clears the floor
  // while no single repeat did was published as though it were solid. The flag
  // is checked in BOTH directions - an unflagged thin row and a flagged solid
  // one are each a finding - so the marker cannot drift away from the data.
  const thin = latency.lowSampleP95 === true || (latency.thinnestRepeat ?? 0) < P95_SAMPLE_FLOOR;
  const declared = attributes.indicative === 'p95';

  if (thin && !declared) {
    errors.push(
      `${where}: p95 rests on ${latency.thinnestRepeat} samples in its thinnest repeat ` +
        `(floor ${P95_SAMPLE_FLOOR}) but the marker does not say indicative=p95`,
    );
  } else if (!thin && declared) {
    errors.push(`${where}: marker says indicative=p95 but every repeat cleared the floor`);
  }

  return thin;
}

function checkStorageRow(errors, where, cells, attributes, summary) {
  const cell = findCell(summary, attributes);

  if (!cell) {
    errors.push(
      `${where}: no measured cell for scenario=${attributes.scenario} ` +
        `seed=${attributes.seed} concurrency=${attributes.concurrency}`,
    );
    return;
  }

  if (cell.bytesPerRun === null || cell.bytesPerRun === undefined) {
    // Autovacuum inside the window makes byte growth unattributable; the
    // measurement says so by storing null, and a null cannot be published.
    errors.push(`${where}: the measured cell has no byte growth (autovacuum interfered)`);
    return;
  }

  checkPathLabel(errors, where, cells[0], attributes.scenario);
  compare(errors, where, 'Rows per run', cells[1], formatRows(cell.rowsPerRun));

  // 🚨 Phase 166's fourth finding: one clean window published as though it were
  // an average of three. The repeat count is part of the published cell, so a
  // page that stops saying it cannot pass.
  const repeats = cell.storageRepeats;
  const plural = repeats === 1 ? 'repeat' : 'repeats';
  compare(
    errors,
    where,
    'Bytes per run',
    cells[2],
    `~${formatKibibytes(cell.bytesPerRun)} KiB (${repeats} clean ${plural})`,
  );
}

function checkArrivalRow(errors, where, cells, attributes, summary) {
  const rate = Number(attributes.rate);
  const measured = (summary.evidence ?? []).filter((entry) => entry.arrivalRatePerSecond === rate);

  if (measured.length === 0) {
    errors.push(`${where}: no measured window at ${attributes.rate} request(s) per second`);
    return;
  }

  // An `invalid` window is one the driver says cannot be trusted
  // (CellStatus.Invalid). The totals summed every window whatever its status,
  // so such a window reached the page unnoticed (phase 174 audit, finding 4).
  const windows = measured.filter((entry) => entry.status !== 'invalid');

  if (windows.length === 0) {
    errors.push(`${where}: every window at ${attributes.rate} request(s) per second is invalid; nothing can be published`);
    return;
  }

  const total = (pick) => windows.reduce((sum, entry) => sum + pick(entry.arrival), 0);

  compare(errors, where, 'Planned/s', cells[0], String(rate));
  compare(
    errors,
    where,
    'Sent',
    cells[1],
    `${formatCount(total((a) => a.sent))} of ${formatCount(total((a) => a.planned))}`,
  );
  compare(errors, where, 'Never sent', cells[2], formatCount(total((a) => a.notSent)));
  compare(errors, where, 'Completed', cells[3], formatCount(total((a) => a.completed)));

  // The queue-wait column summarises three windows. The first rule here only
  // required each published number to sit INSIDE the measured spread, and the
  // audit measured what that permits: for windows of 16.45/16.65/16.71 s the
  // page could publish "16.5 s", "16.7 s", or either end of a range, and all
  // three passed. A summary a reader cannot distinguish from a measurement has
  // to be the measurement: publish the spread, rounded, at both ends.
  const waits = windows.map((entry) => entry.queueWait.p95 / 1000);
  const low = Math.min(...waits);
  const high = Math.max(...waits);
  // Rounded by the page's one rule, so a second rule cannot drift from it.
  const ends = [formatRate(low), formatRate(high)];
  const expected = ends[0] === ends[1] ? `${ends[0]} s` : `${ends[0]}-${ends[1]} s`;

  compare(errors, where, 'Queue wait p95', cells[4].replace(/[‒-―]/g, '-'), expected);
}

// ---------------------------------------------------------------------------
// The gate
// ---------------------------------------------------------------------------

/** Read one measurement artifact, reporting a miss rather than throwing. */
function readArtifact(errors, measurementsRoot, profile, name, seen) {
  const path = join(measurementsRoot, profile, name);

  if (seen.has(path)) {
    return seen.get(path);
  }

  let value = null;

  if (!existsSync(path)) {
    // 🚨 Phase 166's fifth finding was evidence left in an untracked
    // directory. A missing artifact is the finding, never a silent pass.
    errors.push(`${PAGE}: cites profile '${profile}' but ${profile}/${name} is not stored`);
  } else {
    try {
      value = JSON.parse(readFileSync(path, 'utf8'));
    } catch (error) {
      errors.push(`${profile}/${name} is not readable JSON: ${error.message}`);
    }
  }

  seen.set(path, value);
  return value;
}

/**
 * Check every capacity number `guides/production.md` publishes.
 *
 * @param docsRoot the site's `src/content/docs`
 * @param measurementsRoot `bench/capacity/measurements`
 * @param expectedRows how many rows of each kind the page must publish; only a
 *   test fixture passes its own, and the shipped page is checked against the
 *   real inventory above
 * @returns one string per finding; empty when the page matches the artifacts
 */
export function checkCapacityStamp(docsRoot, measurementsRoot, expectedRows = EXPECTED_ROWS) {
  const errors = [];
  const pagePath = join(docsRoot, PAGE);

  if (!existsSync(pagePath)) {
    return [`${PAGE} is missing; the capacity gate cannot check a page that is not there`];
  }

  const text = readFileSync(pagePath, 'utf8');
  const lines = text.split('\n');
  const seen = new Map();
  const summaryFor = (profile) =>
    readArtifact(errors, measurementsRoot, profile, 'summary.json', seen);

  const counted = { latency: 0, arrival: 0, storage: 0, value: 0 };
  let markedRows = 0;

  for (const table of findTables(lines)) {
    const body = table.lines.slice(2);
    const marked = body.filter(({ line }) => MARKER.test(line));

    if (marked.length === 0) {
      // 🚨 A whole table with no markers used to be invisible: the audit added
      // a fourth capacity table of invented numbers and the gate stayed green.
      // The header is the signature - a table shaped like a capacity table is
      // one, whether or not anybody marked its rows.
      const header = splitCells(table.lines[0].line).join(' | ');
      const shape = Object.entries(SCHEMAS).find(([, columns]) => columns.join(' | ') === header);

      if (shape) {
        errors.push(
          `${PAGE}:${table.start + 1}: a '${shape[0]}' table publishes ${body.length} row(s) ` +
            'and none carries a source marker',
        );
      }

      continue;
    }

    // 🚨 The rule that keeps the gate from being quietly outgrown: once a table
    // publishes capacity numbers, EVERY row in it must name its source. A row
    // appended later cannot slip past by simply not carrying a marker.
    for (const { line, number } of body) {
      if (!MARKER.test(line)) {
        errors.push(`${PAGE}:${number}: capacity table row carries no source marker`);
      }
    }

    const kinds = new Set();
    let anyThin = false;

    for (const { line, number } of marked) {
      const where = `${PAGE}:${number}`;
      const { attributes, error } = parseMarker(line.match(MARKER)[1]);

      if (error) {
        errors.push(`${where}: ${error}`);
        continue;
      }

      const { kind, profile } = attributes;

      if (!SCHEMAS[kind]) {
        errors.push(`${where}: marker names kind '${kind}', which the gate does not know`);
        continue;
      }

      kinds.add(kind);
      counted[kind] += 1;
      markedRows += 1;

      if (!profile) {
        errors.push(`${where}: marker names no profile`);
        continue;
      }

      const summary = summaryFor(profile);

      if (!summary) {
        continue;
      }

      const cells = splitCells(line);
      const schema = SCHEMAS[kind];

      if (cells.length !== schema.length) {
        errors.push(`${where}: row has ${cells.length} cells, schema '${kind}' has ${schema.length}`);
        continue;
      }

      if (kind === 'latency') {
        anyThin = checkLatencyRow(errors, where, cells, attributes, summary) || anyThin;
      } else if (kind === 'storage') {
        checkStorageRow(errors, where, cells, attributes, summary);
      } else {
        checkArrivalRow(errors, where, cells, attributes, summary);
      }
    }

    if (kinds.size > 1) {
      errors.push(
        `${PAGE}:${table.start + 1}: one table mixes marker kinds ${[...kinds].sort().join(', ')}`,
      );
      continue;
    }

    if (kinds.size === 0) {
      // Every marker in this table was unusable, and each one has already been
      // reported. There is no schema to check the header against.
      continue;
    }

    const [kind] = kinds;
    const schema = SCHEMAS[kind];
    const header = splitCells(table.lines[0].line);

    if (header.join(' | ') !== schema.join(' | ')) {
      errors.push(
        `${PAGE}:${table.start + 1}: '${kind}' table header is '${header.join(' | ')}', ` +
          `expected '${schema.join(' | ')}'`,
      );
    }

    // A thin p95 must reach the reader, not only the marker. The prose that
    // follows the table is where this page already says it.
    if (anyThin) {
      const after = lines.slice(table.start + table.lines.length, table.start + table.lines.length + 8);

      if (!after.join(' ').toLowerCase().includes('indicative')) {
        errors.push(
          `${PAGE}:${table.start + 1}: a row's p95 is indicative, but the text after the ` +
            'table does not tell the reader so',
        );
      }
    }
  }

  if (markedRows === 0) {
    // Zero marked rows cannot mean a clean page: it means the markers were
    // lost, and a gate that passes on an empty inventory checks nothing.
    errors.push(`${PAGE}: no capacity source markers found; refusing to verify zero numbers`);
    return errors;
  }

  for (const [kind, expected] of Object.entries(expectedRows)) {
    if (counted[kind] !== expected) {
      errors.push(
        `${PAGE}: ${counted[kind]} '${kind}' row(s) carry a marker, expected ${expected}. ` +
          'Re-measuring changes this number in check-capacity-stamp.mjs, with the page',
      );
    }
  }

  errors.push(...checkProseValues(lines, summaryFor, counted));
  errors.push(...checkStamps(text, measurementsRoot, seen, errors));
  return errors;
}

/**
 * Numbers the page states in prose, each marked with the field it quotes.
 *
 * 🚨 This exists because the audit found the page calling two p95 values "a
 * p99" - phase 166's own defect class, still open, in a sentence no table gate
 * could ever see. A number outside a table is still a published number.
 *
 *     a p99 of 1721 ms<!-- capacity: kind=value … field=latency.p99 -->
 */
function checkProseValues(lines, summaryFor, counted) {
  const errors = [];

  lines.forEach((line, index) => {
    if (line.trimStart().startsWith('|')) {
      return;
    }

    for (const match of line.matchAll(new RegExp(MARKER.source, 'g'))) {
      const where = `${PAGE}:${index + 1}`;
      const { attributes, error } = parseMarker(match[1]);

      if (error) {
        errors.push(`${where}: ${error}`);
        continue;
      }

      if (attributes.kind !== 'value') {
        errors.push(`${where}: a marker outside a table must be kind=value, not '${attributes.kind}'`);
        continue;
      }

      counted.value += 1;
      const summary = summaryFor(attributes.profile);

      if (!summary) {
        continue;
      }

      // The number this marker speaks for is the one it sits directly behind.
      const before = line.slice(0, match.index);
      const quoted = before.match(/([\d]+(?:[ \d]*\d)?(?:\.\d+)?)\s*(ms|s|%|KiB)?$/);

      if (!quoted) {
        errors.push(`${where}: kind=value marker follows no number`);
        continue;
      }

      const expected =
        attributes.field === 'p50-spread'
          ? spreadPercent(summary)
          : measuredValue(summary, attributes);

      if (expected.error) {
        errors.push(`${where}: ${expected.error}`);
      } else if (attributes.field === 'p50-spread') {
        // A ceiling claim: "within about N%" must not understate the spread.
        if (Number(quoted[1]) < expected.value) {
          errors.push(
            `${where}: claims within ${quoted[1]}%, but the widest measured p50 gap is ` +
              `${expected.value.toFixed(2)}% (${expected.worst})`,
          );
        }
      } else {
        compare(errors, where, `prose ${attributes.field}`, quoted[1], expected.value);
      }
    }
  });

  return errors;
}

/** One measured field, formatted the way the page writes it. */
function measuredValue(summary, attributes) {
  const cell = findCell(summary, attributes);

  if (!cell) {
    return {
      error:
        `names no measured cell (scenario=${attributes.scenario} ` +
        `seed=${attributes.seed} concurrency=${attributes.concurrency})`,
    };
  }

  const path = String(attributes.field).split('.');
  const leaf = path[path.length - 1];
  const value = path.reduce((node, key) => (node == null ? node : node[key]), cell);

  if (value === undefined || value === null) {
    return { error: `names field '${attributes.field}', which the measurement does not carry` };
  }

  const format = VALUE_FORMATS[leaf];

  if (!format) {
    return { error: `names field '${attributes.field}', which the gate cannot format` };
  }

  return { value: format(value).replace(/ ms$/, '') };
}

/** The widest relative p50 gap between the empty and the full database. */
function spreadPercent(summary) {
  const empty = new Map();
  let value = 0;
  let worst = 'none';

  for (const row of summary.rows ?? []) {
    if (row.seedShape === 'empty') {
      empty.set(`${row.scenario}/${row.concurrency}`, row.latency.p50);
    }
  }

  for (const row of summary.rows ?? []) {
    const key = `${row.scenario}/${row.concurrency}`;

    if (row.seedShape !== 'full' || !empty.has(key)) {
      continue;
    }

    const gap = (Math.abs(row.latency.p50 - empty.get(key)) / empty.get(key)) * 100;

    if (gap > value) {
      value = gap;
      worst = key;
    }
  }

  return { value, worst };
}

/** The commit stamps and the environment sentence the page states in prose. */
function checkStamps(text, measurementsRoot, seen, collected) {
  const errors = [];
  const section = text.slice(text.indexOf('## What one environment measured'));
  const raw = section.slice(0, indexOfNextHeading(section));
  // A line break is not meaningful to a prose claim: "16 GiB,\n.NET 10.0.100"
  // states the same two facts as one unwrapped line, and a gate that missed
  // one because the paragraph was rewrapped would be reporting the wrap.
  const body = raw.replace(/\s+/g, ' ');
  const manifests = [];

  // The profiles are whatever is stored, not a list kept here. A list here
  // would silently stop covering a profile added to the bench later, which is
  // the drift this gate exists to prevent.
  for (const profile of storedProfiles(measurementsRoot)) {
    const manifest = readArtifact(collected, measurementsRoot, profile, 'manifest.json', seen);

    if (manifest) {
      manifests.push([profile, manifest]);
    }
  }

  if (manifests.length === 0) {
    return [`${PAGE}: no measurement manifest is stored; every stamp on the page is unbacked`];
  }

  // 1 - every short SHA the page names belongs to a run that is stored.
  const commits = new Set(manifests.map(([, manifest]) => manifest.commit));

  // Seven characters, not eight: packageVersion carries a seven-character
  // SHA, and a stamp in that form went unchecked (phase 174 audit, finding 3).
  for (const [stamp] of body.matchAll(/(?<![0-9a-zA-Z])[0-9a-f]{7,40}(?![0-9a-zA-Z])/g)) {
    if (![...commits].some((commit) => commit.startsWith(stamp))) {
      errors.push(`${PAGE}: commit stamp '${stamp}' matches no stored manifest`);
    }
  }

  // And the other direction: a stored run the page never names is a run whose
  // numbers the reader cannot attribute.
  for (const [profile, manifest] of manifests) {
    if (!body.includes(manifest.commit.slice(0, 8))) {
      errors.push(`${PAGE}: profile '${profile}' measured ${manifest.commit.slice(0, 8)}, which the page never names`);
    }
  }

  // 2 - the environment sentence. Direction is one way on purpose (§174.3):
  // every measured value must appear, and the page may say more besides.
  for (const [field, render] of ENVIRONMENT_FIELDS) {
    const stated = new Map();

    for (const [profile, manifest] of manifests) {
      const value = render(manifest[field]);
      stated.set(value, [...(stated.get(value) ?? []), profile]);
    }

    if (stated.size > 1) {
      // One sentence cannot describe two machines. The page is not checked for
      // this field at all: there is no single right answer for it to state, and
      // naming one run as the reference would report the wrong profile.
      const split = [...stated]
        .map(([value, profiles]) => `${profiles.sort().join('/')}=${value}`)
        .sort()
        .join(', ');
      errors.push(
        `${PAGE}: the stored runs disagree on ${field} (${split}); ` +
          'one environment sentence cannot state both',
      );
      continue;
    }

    const [expected] = stated.keys();

    if (!body.includes(expected)) {
      errors.push(`${PAGE}: the environment paragraph never states ${field} '${expected}'`);
    }
  }

  return errors;
}

/** Every profile directory that stores a measurement, in a stable order. */
function storedProfiles(measurementsRoot) {
  if (!existsSync(measurementsRoot)) {
    return [];
  }

  return readdirSync(measurementsRoot, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name)
    .sort();
}

/** Where the capacity section ends: the next heading of the same level. */
function indexOfNextHeading(section) {
  const match = section.slice(1).match(/\n## /);
  return match ? match.index + 1 : section.length;
}
