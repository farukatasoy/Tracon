// Runs the protocol SDKs and the TypeScript client against the reference host, one smoke scenario or sweep per suite.
//
// Usage: node run.mjs [--suite client|ag-ui|a2a|mcp ...] [--no-build] [--base-url <url>] [--report <directory>]
//
// Explicit and opt-in: it needs the .NET SDK to build the host, `npm ci` here and in packages/tracon-client (the
// client is read from its built `dist/`), and it opens a loopback port. Nothing in this directory is published.
// The exit code is 1 when any check fails.

import { mkdirSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { parseArgs } from 'node:util';

import { buildHost, repositoryRoot, startHost } from './lib/host.mjs';

const SUITES = {
  client: './suites/client-roundtrip.mjs',
  'ag-ui': './suites/ag-ui.mjs',
  a2a: './suites/a2a.mjs',
  mcp: './suites/mcp.mjs',
};

const { values } = parseArgs({
  options: {
    suite: { type: 'string', multiple: true },
    'no-build': { type: 'boolean', default: false },
    'base-url': { type: 'string' },
    report: { type: 'string', default: join(repositoryRoot, 'artifacts', 'sdk-conformance') },
  },
});

const selected = values.suite?.length ? values.suite : Object.keys(SUITES);
const unknown = selected.filter((name) => !(name in SUITES));

if (unknown.length > 0) {
  console.error(`Unknown suite: ${unknown.join(', ')}. Known: ${Object.keys(SUITES).join(', ')}.`);
  process.exit(2);
}

const report = resolve(values.report);

mkdirSync(report, { recursive: true });

let host = null;
let baseUrl = values['base-url'];

if (!baseUrl) {
  if (!values['no-build']) {
    buildHost();
  }

  host = await startHost(join(report, 'host.log'));
  baseUrl = host.baseUrl;
}

let failed = 0;

try {
  const document = await (await fetch(`${baseUrl}/openapi/v1.json`)).json();

  for (const name of selected) {
    const { run, KNOWN_OPEN: known = new Map() } = await import(SUITES[name]);
    const started = Date.now();
    const outcome = await run({ baseUrl, document, report });

    // A check that fails today and is written up stays on the suite's known-open list: it does not fail the run, and it
    // fails the run the day it passes, so the list only shrinks.
    for (const result of outcome.results) {
      const reason = known.get(result.id);

      if (reason === undefined) {
        continue;
      }

      if (result.ok) {
        result.ok = false;
        result.detail = 'it passes now; take it off the known-open list';
      } else {
        result.ok = true;
        result.known = true;
        result.detail = `${reason} (${result.detail})`;
      }
    }

    const bad = outcome.results.filter((result) => !result.ok);

    console.log(`\n== ${name}: ${outcome.results.length - bad.length}/${outcome.results.length} checks passed (${((Date.now() - started) / 1000).toFixed(1)} s)`);

    if (outcome.summary) {
      console.log(`   ${JSON.stringify(outcome.summary)}`);
    }

    for (const result of outcome.results) {
      console.log(`   ${result.known ? 'KNOWN' : result.ok ? 'ok   ' : 'FAIL '} ${result.id}${result.detail ? ` - ${String(result.detail).replace(/\s+/gu, ' ').slice(0, 220)}` : ''}`);
    }

    failed += bad.length;

    if (outcome.outcomes) {
      writeFileSync(
        join(report, `${name}-outcomes.tsv`),
        outcome.outcomes.map((o) => [o.kind, o.method, o.path, o.operationId, o.status ?? '', (o.detail ?? '').replace(/\s+/gu, ' ')].join('\t')).join('\n') + '\n',
      );
    }
  }
} finally {
  await host?.stop();
}

process.exit(failed === 0 ? 0 : 1);
