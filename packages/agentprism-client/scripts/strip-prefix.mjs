#!/usr/bin/env node
// The TypeScript counterpart of Faz 83's nswag-prepare-document.py strip_prefix
// step (docs/83-TIPLI-ISTEMCI-VE-CLI.md, section 83.3).
//
// The committed document is generated with every path relative to the DEFAULT
// MapAgentPrism prefix ('/agentprism'), but that prefix is a runtime parameter
// (AgentPrismEndpointRouteBuilderExtensions.cs). A client generated straight
// from the document would 404 against any consumer that calls MapAgentPrism
// with a custom prefix. AgentPrismClientOptions.baseUrl carries the prefix
// instead, so the schema types must describe paths WITHOUT it.
//
// Usage: strip-prefix.mjs <input.json> <output.json> [prefix]

import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

export const DEFAULT_PREFIX = '/agentprism';

/** Mutates `document.paths`, dropping `prefix` from every key. */
export function stripPrefix(document, prefix = DEFAULT_PREFIX) {
  const paths = document.paths ?? {};
  const stripped = {};

  for (const [path, item] of Object.entries(paths)) {
    if (!path.startsWith(prefix)) {
      throw new Error(`Path '${path}' does not start with expected prefix '${prefix}'`);
    }

    const rest = path.slice(prefix.length);
    stripped[rest === '' ? '/' : rest] = item;
  }

  document.paths = stripped;

  return document;
}

function main(argv) {
  if (argv.length !== 2 && argv.length !== 3) {
    console.error(`Usage: strip-prefix.mjs <input.json> <output.json> [prefix]`);
    return 1;
  }

  const [inputPath, outputPath] = argv;
  const prefix = argv[2] ?? DEFAULT_PREFIX;

  const document = stripPrefix(JSON.parse(readFileSync(inputPath, 'utf8')), prefix);

  writeFileSync(outputPath, `${JSON.stringify(document, null, 2)}\n`);

  console.log(`Stripped '${prefix}' from ${Object.keys(document.paths).length} paths -> ${outputPath}`);

  return 0;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  process.exit(main(process.argv.slice(2)));
}
