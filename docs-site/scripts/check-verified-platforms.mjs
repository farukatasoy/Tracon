// The compatibility page promises a platform only when a leg of the CI workflow runs it.
//
// Every row of a table under "Verified platforms" ends in a Run column. The column starts with the identifier of a job in
// the CI workflow, in backticks, and names the cell of that job's matrix when the job has one:
//
//   `postgres-matrix` · cell `13` · 2026-11-04
//
// A row whose job is not in the workflow, or whose cell is not in that job's matrix, is a promise without a leg behind it.
// It happens when a leg is renamed or dropped and the page keeps saying it was run. A job with a matrix must be cited with
// its cell, and a table under the heading without a Run column is an error of its own: it would otherwise drop out of the
// check unseen.

/** Matrix keys whose value names a leg. A leg is named by `name`, or by `os` or `sqlite` when it has no name. */
const CELL_KEYS = ['name', 'os', 'sqlite'];

function unquote(value) {
  return value.replace(/\s+#.*$/, '').trim().replace(/^(['"])(.*)\1$/, '$2');
}

/**
 * The jobs of a workflow and the cell names of each job's matrix: `Map<jobId, Set<cellName>>`.
 *
 * Reads the layout the workflow files of this repository use (jobs at two spaces, the matrix of a job at six). It is a
 * line reader and not a YAML parser on purpose: the site does not depend on one, and the shape it needs is small.
 */
export function readWorkflowCells(workflowText) {
  const jobs = new Map();
  const lines = workflowText.split(/\r?\n/);
  const jobsAt = lines.findIndex((line) => /^jobs:\s*$/.test(line));

  if (jobsAt < 0) return jobs;

  let current = null;
  let matrixIndent = null;

  for (const line of lines.slice(jobsAt + 1)) {
    const job = /^ {2}([A-Za-z0-9_-]+):\s*$/.exec(line);

    if (job) {
      current = new Set();
      jobs.set(job[1], current);
      matrixIndent = null;
      continue;
    }

    if (current === null || /^\s*(#.*)?$/.test(line)) continue;

    const indent = line.length - line.trimStart().length;

    if (matrixIndent !== null && indent <= matrixIndent) matrixIndent = null;

    if (matrixIndent === null) {
      if (/^\s+matrix:\s*$/.test(line)) matrixIndent = indent;
      continue;
    }

    const entry = /^\s*(?:-\s+)?([A-Za-z_][\w-]*):\s*(.*)$/.exec(line);

    if (!entry || !CELL_KEYS.includes(entry[1])) continue;

    const list = /^\[(.*)\]\s*(?:#.*)?$/.exec(entry[2].trim());
    const values = list ? list[1].split(',').map(unquote) : [unquote(entry[2])];

    for (const value of values) if (value !== '') current.add(value);
  }

  return jobs;
}

function splitRow(line) {
  return line.replace(/^\||\|\s*$/g, '').split(/(?<!\\)\|/).map((cell) => cell.trim());
}

/** Every table under "Verified platforms": `[{ first, hasRun, runs: [{ row, run }] }]`. */
export function readTables(compatibilityText) {
  const section = compatibilityText.split(/^## /m).find((part) => part.startsWith('Verified platforms')) ?? '';
  const tables = [];
  let table = null;
  let header = null;

  for (const line of section.split(/\r?\n/)) {
    if (!line.startsWith('|')) {
      table = null;
      header = null;
      continue;
    }

    const cells = splitRow(line);

    if (table === null && header === null) {
      header = cells;
      continue;
    }

    if (table === null) {
      // The row after the header is the separator that makes it a table.
      if (cells.every((cell) => /^:?-+:?$/.test(cell))) {
        table = { first: header[0], hasRun: header.at(-1) === 'Run', runs: [] };
        tables.push(table);
      } else {
        header = null;
      }
      continue;
    }

    table.runs.push({ row: cells[0], run: cells.at(-1) });
  }

  return tables;
}

/** The Run cell of every data row of every table under "Verified platforms" that has a Run column: `[{ row, run }]`. */
export function readRunCells(compatibilityText) {
  return readTables(compatibilityText).filter((table) => table.hasRun).flatMap((table) => table.runs);
}

/** The errors of the Run columns of the "Verified platforms" tables against the workflow's jobs and cells. */
export function checkVerifiedPlatforms(compatibilityText, workflowText) {
  const errors = [];
  const jobs = readWorkflowCells(workflowText);
  const tables = readTables(compatibilityText);
  const runs = readRunCells(compatibilityText);

  // A table the reader cannot recognise would silently drop out of the check, so every table must carry the column.
  for (const table of tables.filter((candidate) => !candidate.hasRun)) {
    errors.push(`compatibility.md "Verified platforms": the table that starts with "${table.first}" has no Run column, so none of its rows is checked`);
  }

  if (jobs.size === 0) {
    errors.push('compatibility.md "Verified platforms": the CI workflow has no jobs the gate can read');
  }

  if (runs.length === 0) {
    errors.push('compatibility.md "Verified platforms": no table with a Run column was found, so no row is checked');
  }

  for (const { row, run } of runs) {
    const label = `compatibility.md "Verified platforms", row "${row}"`;
    const job = /^`([a-z][a-z0-9-]*)`/.exec(run);

    if (!job) {
      errors.push(`${label}: the Run column does not start with the CI job in backticks (found "${run}")`);
      continue;
    }

    const cells = jobs.get(job[1]);

    if (cells === undefined) {
      errors.push(`${label}: job \`${job[1]}\` is not in the CI workflow`);
      continue;
    }

    const cell = /^`[a-z][a-z0-9-]*` · cell `([^`]+)`/.exec(run);

    if (cells.size > 0 && !cell) {
      errors.push(`${label}: job \`${job[1]}\` has a matrix, so the Run column must name the cell as \`${job[1]}\` · cell \`<name>\` (cells: ${[...cells].join(', ')})`);
    } else if (cell && !cells.has(cell[1])) {
      errors.push(`${label}: cell \`${cell[1]}\` is not in the matrix of job \`${job[1]}\` (cells: ${[...cells].join(', ') || 'none'})`);
    }
  }

  return errors;
}
