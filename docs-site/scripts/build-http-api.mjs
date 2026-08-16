// Turns docs/openapi/agentprism.json into Starlight pages — one per tag.
//
// Why not embed an OpenAPI viewer:
//
//   The obvious choice was Scalar. Measured: its standalone browser bundle is 7.4 MB
//   across 90 chunks, it renders in its own theme, and — the part that decided it —
//   nothing inside it reaches Pagefind. Every operation on this API carries a written
//   description; hiding all 143 of them from the site's own search to gain a
//   try-it-out console was the wrong trade.
//
//   The raw document is still published at /openapi/agentprism.json, so anyone who
//   wants Scalar, Swagger UI, Postman, or a generated client can load it there.

import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync, copyFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(here, '../..');
const documentPath = join(repositoryRoot, 'docs/openapi/agentprism.json');
const outputDirectory = join(here, '../src/content/docs/http-api');
const publicDirectory = join(here, '../public/openapi');
const sidebarFile = join(here, '../src/generated/http-api-sidebar.json');

// See build-api-reference.mjs: markdown links carry the site base, sidebar links do
// not — Starlight prepends it.
const base = '/AgentPrism/http-api';
const sidebarBase = '/http-api';
const methods = ['get', 'post', 'put', 'patch', 'delete'];

main();

function main() {
  if (!existsSync(documentPath)) {
    throw new Error(`${documentPath} not found. Refresh the OpenAPI snapshot first.`);
  }

  const document = JSON.parse(readFileSync(documentPath, 'utf8'));
  const groups = groupByTag(document);

  rmSync(outputDirectory, { recursive: true, force: true });
  mkdirSync(outputDirectory, { recursive: true });
  mkdirSync(publicDirectory, { recursive: true });
  copyFileSync(documentPath, join(publicDirectory, 'agentprism.json'));

  for (const [tag, operations] of groups) {
    writeFileSync(join(outputDirectory, `${slugify(tag)}.md`), renderGroup(tag, operations, document));
  }

  mkdirSync(dirname(sidebarFile), { recursive: true });
  writeFileSync(
    sidebarFile,
    `${JSON.stringify(
      [...groups.entries()].map(([tag, operations]) => ({
        label: `${tag} (${operations.length})`,
        link: `${sidebarBase}/${slugify(tag)}/`,
      })),
      null,
      2,
    )}\n`,
  );

  const total = [...groups.values()].reduce((sum, operations) => sum + operations.length, 0);
  console.log(`HTTP API: ${total} operations across ${groups.size} groups.`);
}

/**
 * Every operation carries the tag "AgentPrism" plus one area tag; the area tag is
 * what a reader navigates by, so that is the one the pages are grouped on.
 */
function groupByTag(document) {
  const groups = new Map();

  for (const [path, item] of Object.entries(document.paths)) {
    for (const method of methods) {
      const operation = item[method];

      if (!operation) {
        continue;
      }

      const tag = (operation.tags ?? []).find((candidate) => candidate !== 'AgentPrism') ?? 'Other';
      groups.set(tag, [...(groups.get(tag) ?? []), { path, method, operation }]);
    }
  }

  return new Map(
    [...groups.entries()]
      .sort((left, right) => left[0].localeCompare(right[0]))
      .map(([tag, operations]) => [
        tag,
        operations.sort((left, right) =>
          left.path === right.path
            ? methods.indexOf(left.method) - methods.indexOf(right.method)
            : left.path.localeCompare(right.path),
        ),
      ]),
  );
}

function renderGroup(tag, operations, document) {
  const lines = [
    '---',
    `title: ${quote(tag)}`,
    `description: ${quote(`The ${tag.toLowerCase()} endpoints of the AgentPrism HTTP API — ${operations.length} operations.`)}`,
    `slug: http-api/${slugify(tag)}`,
    'editUrl: false',
    'lastUpdated: false',
    '---',
    '',
    `${operations.length} operations. Every path is shown with the \`/agentprism\` prefix,`,
    'which is whatever you passed to `MapAgentPrism`.',
    '',
  ];

  for (const { path, method, operation } of operations) {
    lines.push(`## ${method.toUpperCase()} ${path.replace('/agentprism', '')}`);
    lines.push('');

    if (operation.summary) {
      lines.push(`**${operation.summary}**`);
      lines.push('');
    }

    if (operation.description) {
      lines.push(operation.description);
      lines.push('');
    }

    const parameters = operation.parameters ?? [];

    if (parameters.length > 0) {
      lines.push('| Parameter | In | Required | Type |');
      lines.push('|---|---|---|---|');

      for (const parameter of parameters) {
        lines.push(
          `| \`${parameter.name}\` | ${parameter.in} | ${parameter.required ? 'yes' : 'no'} | ` +
            `${describeSchema(parameter.schema, document)} |`,
        );
      }

      lines.push('');
    }

    const requestBody = operation.requestBody;

    if (requestBody) {
      const content = Object.entries(requestBody.content ?? {})[0];

      if (content) {
        lines.push(
          `**Request body** (${requestBody.required ? 'required' : 'optional'}): ` +
            `\`${content[0]}\` → ${describeSchema(content[1]?.schema, document)}`,
        );
        lines.push('');
      }
    }

    lines.push('| Response | Body |');
    lines.push('|---|---|');

    for (const [status, response] of Object.entries(operation.responses ?? {})) {
      const content = Object.entries(response.content ?? {})[0];
      const body = content ? `\`${content[0]}\` → ${describeSchema(content[1]?.schema, document)}` : '—';
      lines.push(`| **${status}** ${response.description ?? ''} | ${body} |`);
    }

    lines.push('');
  }

  return lines.join('\n');
}

/** A readable one-liner for a schema: the type name if it has one, the shape if not. */
function describeSchema(schema, document) {
  if (!schema) {
    return '—';
  }

  if (schema.$ref) {
    const name = schema.$ref.split('/').pop();
    return `\`${name}\``;
  }

  if (schema.type === 'array') {
    return `array of ${describeSchema(schema.items, document)}`;
  }

  if (Array.isArray(schema.enum)) {
    return `enum: ${schema.enum.map((value) => `\`${value}\``).join(', ')}`;
  }

  const type = Array.isArray(schema.type) ? schema.type.filter((entry) => entry !== 'null')[0] : schema.type;

  return type ? `\`${type}${schema.format ? ` (${schema.format})` : ''}\`` : '`object`';
}

function slugify(text) {
  return text.toLowerCase().replaceAll(/[^a-z0-9]+/g, '-').replaceAll(/^-|-$/g, '');
}

function quote(text) {
  return `"${text.replaceAll('\\', '\\\\').replaceAll('"', '\\"')}"`;
}
