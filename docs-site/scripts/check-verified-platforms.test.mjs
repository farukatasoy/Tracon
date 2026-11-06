import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { checkVerifiedPlatforms, readRunCells, readTables, readWorkflowCells } from './check-verified-platforms.mjs';

const WORKFLOW = `name: CI
jobs:
  build:
    name: Build (\${{ matrix.os }})
    strategy:
      matrix:
        os: [ubuntu-latest, windows-latest]
    steps:
      - run: echo
  postgres-matrix:
    name: PostgreSQL (\${{ matrix.name }})
    strategy:
      fail-fast: false
      matrix:
        include:
          - name: '12'
            image: pgvector/pgvector:pg12
          # a comment inside the matrix
          - name: '13'
            image: pgvector/pgvector:pg13
          - name: '18, ICU tr-TR'
            image: ''
    env:
      NAME: not-a-cell
  sqlite-floor:
    strategy:
      matrix:
        include:
          - native: '2.1.7'
            sqlite: '3.44.0'
  culture-matrix:
    strategy:
      matrix:
        include:
          - name: tr-TR, America/New_York
            tz: America/New_York
  release-dryrun:
    steps:
      - run: echo
`;

const PAGE = `## Storage engines

| Capability | Run |
|---|---|
| unrelated | \`nothing\` |

## Verified platforms

### PostgreSQL

| Server | Result | Run |
|---|---|---|
| 12.19 | Verified | \`postgres-matrix\` · cell \`12\` · 2026-11-04 |
| 13.23 | Verified | \`postgres-matrix\` · cell \`13\` · 2026-11-04 |
| 18.4, ICU | Verified | \`postgres-matrix\` · cell \`18, ICU tr-TR\` · 2026-11-04 |

### Other

| Cell | Result | Run |
|---|---|---|
| Linux | Verified | \`build\` · cell \`ubuntu-latest\` |
| Windows | Not verified | \`build\` · cell \`windows-latest\` |
| SQLite floor | Verified | \`sqlite-floor\` · cell \`3.44.0\` · 2026-11-04 |
| Culture | Verified | \`culture-matrix\` · cell \`tr-TR, America/New_York\` · the projects that need no Docker |
| No cell named | Verified | \`release-dryrun\` |

## Agent Framework dependency versions

| Package | Run |
|---|---|
| \`Tracon.Core\` | \`not-checked\` |
`;

test('the workflow reader finds jobs and their cell names, and ignores other matrix values', () => {
  const jobs = readWorkflowCells(WORKFLOW);

  assert.deepEqual([...jobs.keys()], ['build', 'postgres-matrix', 'sqlite-floor', 'culture-matrix', 'release-dryrun']);
  assert.deepEqual([...jobs.get('build')], ['ubuntu-latest', 'windows-latest']);
  assert.deepEqual([...jobs.get('postgres-matrix')], ['12', '13', '18, ICU tr-TR']);
  assert.deepEqual([...jobs.get('sqlite-floor')], ['3.44.0']);
  assert.deepEqual([...jobs.get('culture-matrix')], ['tr-TR, America/New_York']);
  assert.deepEqual([...jobs.get('release-dryrun')], []);
});

test('only the tables under "Verified platforms" with a Run column are read', () => {
  const runs = readRunCells(PAGE);

  assert.equal(runs.length, 8);
  assert.ok(runs.every((run) => run.run.startsWith('`')));
  assert.ok(!runs.some((run) => run.run.includes('not-checked') || run.run.includes('nothing')));
});

test('a page whose every row names a job and a cell of the workflow is clean', () => {
  assert.deepEqual(checkVerifiedPlatforms(PAGE, WORKFLOW), []);
});

test('a cell removed from the matrix turns the rows that cite it red', () => {
  const without13 = WORKFLOW.replace("          - name: '13'\n            image: pgvector/pgvector:pg13\n", '');
  const errors = checkVerifiedPlatforms(PAGE, without13);

  assert.equal(errors.length, 1);
  assert.match(errors[0], /row "13\.23".*cell `13` is not in the matrix of job `postgres-matrix`/);
});

test('a renamed or dropped job turns its rows red', () => {
  const renamed = WORKFLOW.replace('  sqlite-floor:', '  sqlite-lowest:');
  const errors = checkVerifiedPlatforms(PAGE, renamed);

  assert.equal(errors.length, 1);
  assert.match(errors[0], /row "SQLite floor".*job `sqlite-floor` is not in the CI workflow/);
});

test('a Run cell that does not start with a job is red', () => {
  const errors = checkVerifiedPlatforms(PAGE.replace('`build` · cell `ubuntu-latest`', 'job build runs it'), WORKFLOW);

  assert.equal(errors.length, 1);
  assert.match(errors[0], /row "Linux".*does not start with the CI job/);
});

test('a job with a matrix must be cited with its cell', () => {
  const noCell = checkVerifiedPlatforms(PAGE.replace('`postgres-matrix` · cell `12` · 2026-11-04', '`postgres-matrix` · 2026-11-04'), WORKFLOW);
  const unquotedCell = checkVerifiedPlatforms(PAGE.replace('`postgres-matrix` · cell `12`', '`postgres-matrix` · cell 99'), WORKFLOW);

  assert.equal(noCell.length, 1);
  assert.match(noCell[0], /row "12\.19".*job `postgres-matrix` has a matrix, so the Run column must name the cell/);
  assert.equal(unquotedCell.length, 1);
  assert.match(unquotedCell[0], /row "12\.19".*must name the cell/);
});

test('a job without a matrix cannot be cited with a cell', () => {
  const errors = checkVerifiedPlatforms(PAGE.replace('`release-dryrun`', '`release-dryrun` · cell `x`'), WORKFLOW);

  assert.equal(errors.length, 1);
  assert.match(errors[0], /row "No cell named".*cell `x` is not in the matrix of job `release-dryrun` \(cells: none\)/);
});

test('a table under the heading without a Run column is an error and not a silent skip', () => {
  const errors = checkVerifiedPlatforms(PAGE.replace('| Server | Result | Run |', '| Server | Result | Evidence |'), WORKFLOW);

  assert.equal(errors.length, 1);
  assert.match(errors[0], /the table that starts with "Server" has no Run column/);
  assert.equal(readTables(PAGE).length, 2);
});

test('a gate that reads nothing fails instead of passing', () => {
  const noJobs = checkVerifiedPlatforms(PAGE, 'name: CI\n');
  const noTables = checkVerifiedPlatforms('## Verified platforms\n\nNo table here.\n', WORKFLOW);

  assert.match(noJobs[0], /the CI workflow has no jobs the gate can read/);
  assert.equal(noJobs.length, 1 + 8, 'the error above and one per row');
  assert.deepEqual(noTables.map((error) => error.replace(/^.*: /, '')), ['no table with a Run column was found, so no row is checked']);
});

test('the compatibility page of the repository names only legs that ci.yml has', () => {
  const root = resolve(fileURLToPath(new URL('.', import.meta.url)), '../..');
  const page = readFileSync(resolve(root, 'docs-site/src/content/docs/reference/compatibility.md'), 'utf8');
  const workflow = readFileSync(resolve(root, '.github/workflows/ci.yml'), 'utf8');

  assert.deepEqual(checkVerifiedPlatforms(page, workflow), []);
  assert.ok(readRunCells(page).length >= 12);
});
