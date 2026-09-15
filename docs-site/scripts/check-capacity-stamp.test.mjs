import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, readdirSync, statSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { checkCapacityStamp } from './check-capacity-stamp.mjs';

const here = resolve(fileURLToPath(new URL('.', import.meta.url)));
const realDocs = join(here, '../src/content/docs');
const realMeasurements = join(here, '../../bench/capacity/measurements');

const COMMIT = 'abcdef1234567890abcdef1234567890abcdef12';

const MANIFEST = {
  commit: COMMIT,
  packageVersion: '0.0.0-test',
  operatingSystem: 'Darwin 25.6.0',
  architecture: 'arm64',
  processorCount: 10,
  physicalMemoryBytes: 17179869184,
  runtimeVersion: '10.0.100',
  postgreSqlVersion: '18.4 (Debian 18.4-1.pgdg12+1)',
  databaseImage: 'pgvector/pgvector:pg18',
};

/** One measured cell: 1200 samples, so no repeat is thin. */
function cell(overrides = {}) {
  return {
    scenario: 'buffered',
    seedShape: 'empty',
    concurrency: 8,
    repeats: 3,
    storageRepeats: 1,
    completeRepeats: 3,
    latency: {
      count: 1384,
      p50: 1048.19,
      p95: 1084.07,
      lowSampleP95: false,
      thinnestRepeat: 456,
    },
    throughputPerSecond: 7.468,
    bytesPerRun: 8757,
    rowsPerRun: 10.4833,
    ...overrides,
  };
}

const MARKER = 'kind=latency profile=sweep seed=empty scenario=buffered concurrency=8';

/** A page whose one capacity row matches the one measured cell. */
function page({ rows = [`| Buffered | 8 | 1 384 | 1048 ms | 1084 ms | 7.47 <!-- capacity: ${MARKER} --> |`], stamp = 'abcdef12', environment } = {}) {
  const sentence =
    environment ??
    'Measured on Darwin 25.6.0 (Apple Silicon), 10 logical processors, 16 GiB,\n' +
      '.NET 10.0.100, PostgreSQL 18.4 in the `pgvector/pgvector:pg18` image.';

  return [
    '## What one environment measured',
    '',
    sentence,
    '',
    `The load sweep measured Tracon packed from commit \`${stamp}\`.`,
    '',
    '| Path | Concurrency | n | p50 | p95 | Completed/s |',
    '|---|---|---|---|---|---|',
    ...rows,
    '',
    'Prose after the table.',
    '',
    '## Another section',
    '',
  ].join('\n');
}

function fixture(t, { rows, stamp, environment, cells = [cell()], profiles = ['sweep'] } = {}) {
  const root = mkdtempSync(join(tmpdir(), 'tracon-capacity-gate-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));

  const docs = join(root, 'docs');
  const measurements = join(root, 'measurements');
  mkdirSync(join(docs, 'guides'), { recursive: true });
  writeFileSync(join(docs, 'guides/production.md'), page({ rows, stamp, environment }));

  for (const profile of profiles) {
    mkdirSync(join(measurements, profile), { recursive: true });
    writeFileSync(join(measurements, profile, 'manifest.json'), JSON.stringify(MANIFEST));
    writeFileSync(
      join(measurements, profile, 'summary.json'),
      JSON.stringify({ profile, rows: cells, evidence: [] }),
    );
  }

  return { docs, measurements, root };
}

const run = (f, expected = { latency: 1 }) =>
  checkCapacityStamp(f.docs, f.measurements, expected).join('\n');

// --- the defect classes phase 166's audit found ----------------------------

test('a number the page mistyped is a finding', (t) => {
  const f = fixture(t, {
    rows: [`| Buffered | 8 | 1 384 | 9999 ms | 1084 ms | 7.47 <!-- capacity: ${MARKER} --> |`],
  });
  assert.match(run(f), /p50 publishes '9999 ms', measured '1048 ms'/);
});

test('every column is compared, not only the first', (t) => {
  for (const [column, row] of [
    ['n', `| Buffered | 8 | 1 385 | 1048 ms | 1084 ms | 7.47 <!-- capacity: ${MARKER} --> |`],
    ['p95', `| Buffered | 8 | 1 384 | 1048 ms | 1085 ms | 7.47 <!-- capacity: ${MARKER} --> |`],
    ['Completed/s', `| Buffered | 8 | 1 384 | 1048 ms | 1084 ms | 7.48 <!-- capacity: ${MARKER} --> |`],
    ['concurrency', `| Buffered | 9 | 1 384 | 1048 ms | 1084 ms | 7.47 <!-- capacity: ${MARKER} --> |`],
  ]) {
    const f = fixture(t, { rows: [row] });
    assert.match(run(f), new RegExp(`${column.replace('/', '\\/')} publishes`), `${column} went unchecked`);
  }
});

test('a row added without a marker is a finding', (t) => {
  // 🚨 Phase 166's fifth defect class: a number that was never in the gate's
  // inventory at all. Marking the table, not the row, is what closes it.
  const f = fixture(t, {
    rows: [
      `| Buffered | 8 | 1 384 | 1048 ms | 1084 ms | 7.47 <!-- capacity: ${MARKER} --> |`,
      '| Buffered | 64 | 10 944 | 1055 ms | 1120 ms | 59.1 |',
    ],
  });
  assert.match(run(f), /carries no source marker/);
});

test('a commit stamp that matches no stored manifest is a finding', (t) => {
  const f = fixture(t, { stamp: 'deadbeef' });
  assert.match(run(f), /commit stamp 'deadbeef' matches no stored manifest/);
});

test('a stored run the page never names is a finding', (t) => {
  // The other direction: numbers on the page whose provenance a reader cannot
  // attribute, because the page never says that run happened.
  const f = fixture(t, { profiles: ['sweep', 'soak'] });
  const other = join(f.measurements, 'soak/manifest.json');
  writeFileSync(other, JSON.stringify({ ...MANIFEST, commit: `99887766${COMMIT.slice(8)}` }));
  assert.match(run(f), /profile 'soak' measured 99887766, which the page never names/);
});

test('an environment claim that drifts from the manifest is a finding', (t) => {
  const f = fixture(t, {
    environment:
      'Measured on Darwin 25.6.0 (Apple Silicon), 64 logical processors, 16 GiB,\n' +
      '.NET 10.0.100, PostgreSQL 18.4 in the `pgvector/pgvector:pg18` image.',
  });
  assert.match(run(f), /never states processorCount '10 logical processors'/);
});

test('two runs on different machines cannot share one environment sentence', (t) => {
  const f = fixture(t, { profiles: ['sweep', 'soak'] });
  writeFileSync(
    join(f.measurements, 'soak/manifest.json'),
    JSON.stringify({ ...MANIFEST, processorCount: 4 }),
  );
  assert.match(
    run(f),
    /the stored runs disagree on processorCount \(soak=4 logical processors, sweep=10 logical processors\)/,
  );
});

test('a missing measurement directory is a finding, not a crash', (t) => {
  const f = fixture(t);
  rmSync(join(f.measurements, 'sweep'), { recursive: true, force: true });
  assert.match(run(f), /cites profile 'sweep' but sweep\/summary\.json is not stored/);
});

test('unreadable JSON is a finding, not a crash', (t) => {
  const f = fixture(t);
  writeFileSync(join(f.measurements, 'sweep/summary.json'), '{ not json');
  assert.match(run(f), /sweep\/summary\.json is not readable JSON/);
});

test('a marker naming a cell that was never measured is a finding', (t) => {
  const f = fixture(t, {
    rows: [
      '| Buffered | 99 | 1 384 | 1048 ms | 1084 ms | 7.47 ' +
        '<!-- capacity: kind=latency profile=sweep seed=empty scenario=buffered concurrency=99 --> |',
    ],
  });
  assert.match(run(f), /no measured cell for scenario=buffered seed=empty concurrency=99/);
});

// --- the low-sample rule ---------------------------------------------------

test('a p95 no single repeat supported must be declared indicative', (t) => {
  const f = fixture(t, {
    cells: [cell({ latency: { count: 174, p50: 1044.1, p95: 1057.33, lowSampleP95: false, thinnestRepeat: 58 } })],
    rows: [`| Buffered | 8 | 174 | 1044 ms | 1057 ms | 7.47 <!-- capacity: ${MARKER} --> |`],
  });
  assert.match(run(f), /rests on 58 samples in its thinnest repeat .* does not say indicative=p95/);
});

test('a solid p95 cannot be labelled indicative', (t) => {
  const f = fixture(t, {
    rows: [`| Buffered | 8 | 1 384 | 1048 ms | 1084 ms | 7.47 <!-- capacity: ${MARKER} indicative=p95 --> |`],
  });
  assert.match(run(f), /marker says indicative=p95 but every repeat cleared the floor/);
});

test('an indicative p95 must also reach the reader', (t) => {
  const f = fixture(t, {
    cells: [cell({ latency: { count: 174, p50: 1044.1, p95: 1057.33, lowSampleP95: false, thinnestRepeat: 58 } })],
    rows: [`| Buffered | 8 | 174 | 1044 ms | 1057 ms | 7.47 <!-- capacity: ${MARKER} indicative=p95 --> |`],
  });
  // The marker is honest but invisible; the prose after the table says nothing.
  assert.match(run(f), /the text after the table does not tell the reader so/);
});

// --- the gate's own integrity ---------------------------------------------

test('a renamed column stops the gate instead of being read as the old one', (t) => {
  const f = fixture(t);
  const path = join(f.docs, 'guides/production.md');
  writeFileSync(path, readFileSync(path, 'utf8').replace('| p50 | p95 |', '| p95 | p50 |'));
  assert.match(run(f), /table header is .* expected/);
});

test('a table that mixes marker kinds is a finding', (t) => {
  const f = fixture(t, {
    rows: [
      `| Buffered | 8 | 1 384 | 1048 ms | 1084 ms | 7.47 <!-- capacity: ${MARKER} --> |`,
      '| Buffered | 8 | 1 384 | 1048 ms | 1084 ms | 7.47 ' +
        '<!-- capacity: kind=storage profile=sweep seed=empty scenario=buffered concurrency=8 --> |',
    ],
  });
  assert.match(run(f), /mixes marker kinds/);
});

test('a marker that is not key=value is a finding', (t) => {
  const f = fixture(t, {
    rows: ['| Buffered | 8 | 1 384 | 1048 ms | 1084 ms | 7.47 <!-- capacity: latency sweep --> |'],
  });
  assert.match(run(f), /is not key=value/);
});

test('a page with no markers at all fails instead of verifying nothing', (t) => {
  // 🚨 A gate whose inventory is empty reports success while checking zero
  // numbers. That is the shape a gate fails in without anyone noticing.
  const f = fixture(t, { rows: ['| Buffered | 8 | 1 384 | 1048 ms | 1084 ms | 7.47 |'] });
  assert.match(run(f), /refusing to verify zero numbers/);
});

test('rounding comes from one place, so the page and the gate cannot diverge', (t) => {
  // Every rule below is the page's own format, applied to the measured value.
  // If a second rounding rule ever appears, one of these stops matching.
  const cases = [
    [{ throughputPerSecond: 0.9407 }, '0.94'],
    [{ throughputPerSecond: 7.468 }, '7.47'],
    [{ throughputPerSecond: 29.4893 }, '29.5'],
    [{ throughputPerSecond: 59.0777 }, '59.1'],
  ];

  for (const [override, expected] of cases) {
    const f = fixture(t, {
      cells: [cell(override)],
      rows: [`| Buffered | 8 | 1 384 | 1048 ms | 1084 ms | ${expected} <!-- capacity: ${MARKER} --> |`],
    });
    assert.equal(run(f), '', `${override.throughputPerSecond} should publish as ${expected}`);
  }
});

test('the gate changes no file', (t) => {
  // `--denetle` promises to read and not write. A gate that repaired the page
  // would hide exactly the drift it exists to report.
  const f = fixture(t);
  const snapshot = (dir) =>
    readdirSync(dir, { recursive: true })
      .map((entry) => {
        const path = join(dir, entry);
        return statSync(path).isFile() ? `${entry}:${readFileSync(path, 'utf8')}` : entry;
      })
      .sort()
      .join('\n');

  const before = snapshot(f.root);
  checkCapacityStamp(f.docs, f.measurements);
  assert.equal(snapshot(f.root), before);
});

// --- the page this repository actually ships -------------------------------

test("today's shipped page matches its measurements", () => {
  // 🚨 The gate's worst end is a false positive nobody can act on, because the
  // next step is to silence it. This test is the reason that cannot happen
  // quietly: the real page and the real artifacts, with no fixture in between.
  assert.deepEqual(checkCapacityStamp(realDocs, realMeasurements), []);
});

test('the shipped page has a marker on every capacity row', () => {
  const text = readFileSync(join(realDocs, 'guides/production.md'), 'utf8');
  const census = {};

  for (const [, body] of text.matchAll(/<!--\s*capacity:\s*([^>]*?)\s*-->/g)) {
    const kind = body.match(/kind=(\w+)/)[1];
    census[kind] = (census[kind] ?? 0) + 1;
  }

  // 🚨 One number, measured, in one place. The audit found this count written
  // three different ways across three documents and all three were wrong.
  assert.deepEqual(census, { latency: 12, arrival: 4, storage: 3, value: 5 });
});

// --- storage, arrival and prose: the paths the phase-174 audit found untested -

/** A page carrying one storage row, one arrival row and one prose number. */
function widePage({ storage, arrival, prose } = {}) {
  const S = 'kind=storage profile=sweep seed=empty scenario=buffered concurrency=8';
  const A = 'kind=arrival profile=sweep rate=1';
  return [
    '## What one environment measured',
    '',
    'Measured on Darwin 25.6.0 (Apple Silicon), 10 logical processors, 16 GiB,',
    '.NET 10.0.100, PostgreSQL 18.4 in the `pgvector/pgvector:pg18` image.',
    '',
    'The load sweep measured Tracon packed from commit `abcdef12`.',
    '',
    `A p95 of ${prose ?? '1084'} ms<!-- capacity: kind=value profile=sweep seed=empty ` +
      'scenario=buffered concurrency=8 field=latency.p95 --> was measured.',
    '',
    '| Planned/s | Sent | Never sent | Completed | Queue wait p95 |',
    '|---|---|---|---|---|',
    `${arrival ?? '| 1 | 60 of 60 | 0 | 60 | 0.10 s '}<!-- capacity: ${A} --> |`,
    '',
    '| Path | Rows per run (3 repeats) | Bytes per run | Largest contributor |',
    '|---|---|---|---|',
    `${storage ?? '| Buffered | 10.5 | ~8.6 KiB (1 clean repeat) | `run_events` '}<!-- capacity: ${S} --> |`,
    '',
    '## Another section',
    '',
  ].join('\n');
}

function wide(t, options = {}) {
  const f = fixture(t);
  writeFileSync(join(f.docs, 'guides/production.md'), widePage(options));
  writeFileSync(
    join(f.measurements, 'sweep/summary.json'),
    JSON.stringify({
      profile: 'sweep',
      rows: [cell(options.cellOverrides)],
      evidence: [
        { arrivalRatePerSecond: 1, arrival: { planned: 60, sent: 60, notSent: 0, completed: 60 }, queueWait: { p95: 100 } },
      ],
    }),
  );
  return f;
}

const wideRun = (f) => run(f, { storage: 1, arrival: 1 });

test('a storage row that mistypes its row count is a finding', (t) => {
  const f = wide(t, { storage: '| Buffered | 99.9 | ~8.6 KiB (1 clean repeat) | `run_events` ' });
  assert.match(wideRun(f), /Rows per run publishes '99\.9', measured '10\.5'/);
});

test('a storage row that presents one window as an average is a finding', (t) => {
  // 🚨 Phase 166's fourth defect, which its own closure did not close.
  const f = wide(t, { storage: '| Buffered | 10.5 | ~8.6 KiB (3 clean repeats) | `run_events` ' });
  assert.match(wideRun(f), /Bytes per run publishes .* measured '~8\.6 KiB \(1 clean repeat\)'/);
});

test('a storage row cannot publish growth autovacuum made unattributable', (t) => {
  const f = wide(t, { cellOverrides: { bytesPerRun: null } });
  assert.match(wideRun(f), /no byte growth \(autovacuum interfered\)/);
});

test('a storage row labelled for another path is a finding', (t) => {
  const f = wide(t, { storage: '| Streaming | 10.5 | ~8.6 KiB (1 clean repeat) | `run_events` ' });
  assert.match(wideRun(f), /labelled 'Streaming' but cites scenario 'buffered'/);
});

test('an arrival row that mistypes a total is a finding', (t) => {
  const f = wide(t, { arrival: '| 1 | 61 of 60 | 0 | 60 | 0.10 s ' });
  assert.match(wideRun(f), /Sent publishes '61 of 60', measured '60 of 60'/);
});

test('an arrival row must publish the measured queue-wait spread', (t) => {
  const f = wide(t, { arrival: '| 1 | 60 of 60 | 0 | 60 | 0.09 s ' });
  assert.match(wideRun(f), /Queue wait p95 publishes '0\.09 s', measured '0\.10 s'/);
});

test('an arrival marker naming an unmeasured rate is a finding', (t) => {
  const f = fixture(t);
  writeFileSync(
    join(f.docs, 'guides/production.md'),
    widePage().replace('rate=1 -->', 'rate=64 -->'),
  );
  writeFileSync(
    join(f.measurements, 'sweep/summary.json'),
    JSON.stringify({ profile: 'sweep', rows: [cell()], evidence: [] }),
  );
  assert.match(wideRun(f), /no measured window at 64 request\(s\) per second/);
});

test('a number quoted in prose is compared like a table cell', (t) => {
  // 🚨 The audit found the page calling two p95 values "a p99" - a published
  // number no table gate could see.
  const f = wide(t, { prose: '9999' });
  assert.match(wideRun(f), /prose latency\.p95 publishes '9999', measured '1084'/);
});

test('a prose marker naming a field the measurement lacks is a finding', (t) => {
  const f = fixture(t);
  writeFileSync(
    join(f.docs, 'guides/production.md'),
    widePage().replace('field=latency.p95', 'field=latency.p42'),
  );
  writeFileSync(
    join(f.measurements, 'sweep/summary.json'),
    JSON.stringify({ profile: 'sweep', rows: [cell()], evidence: [] }),
  );
  assert.match(wideRun(f), /names field 'latency\.p42', which the measurement does not carry/);
});

test('a prose marker that follows no number is a finding', (t) => {
  const f = fixture(t);
  writeFileSync(
    join(f.docs, 'guides/production.md'),
    widePage().replace(/A p95 of \d+ ms/, 'A p95 of about that'),
  );
  assert.match(wideRun(f), /kind=value marker follows no number/);
});

test('a table kind used outside a table is a finding', (t) => {
  const f = fixture(t);
  writeFileSync(
    join(f.docs, 'guides/production.md'),
    widePage().replace('kind=value profile=sweep seed=empty', 'kind=latency profile=sweep seed=empty'),
  );
  assert.match(wideRun(f), /a marker outside a table must be kind=value, not 'latency'/);
});

test('a whole table with no markers is still inventoried', (t) => {
  // 🚨 Measured by the audit: a fourth capacity table of invented numbers
  // passed, because the gate only looked at tables somebody had marked.
  const f = fixture(t);
  const path = join(f.docs, 'guides/production.md');
  writeFileSync(
    path,
    readFileSync(path, 'utf8').replace(
      '## Another section',
      '| Path | Concurrency | n | p50 | p95 | Completed/s |\n|---|---|---|---|---|---|\n' +
        '| Invented | 256 | 99 999 | 1 ms | 2 ms | 999 |\n\n## Another section',
    ),
  );
  assert.match(run(f), /a 'latency' table publishes 1 row\(s\) and none carries a source marker/);
});

test('deleting markers makes the gate louder, not quieter', (t) => {
  // The empty-inventory guard alone did not cover this: with other kinds still
  // marked, a page could drop every latency marker and stay green.
  const f = fixture(t);
  assert.match(run(f, { latency: 12 }), /1 'latency' row\(s\) carry a marker, expected 12/);
});
