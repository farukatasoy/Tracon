// Turns docs/openapi/agentprism.json into Starlight pages — one per tag and schema.
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

import { existsSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(here, '../..');
const documentPath = join(repositoryRoot, 'docs/openapi/agentprism.json');
const endpointSourceDirectory = join(repositoryRoot, 'src/AgentPrism.AspNetCore');
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

  const authorizationByOperation = readEndpointAuthorization();
  const document = normalizeDocument(
    JSON.parse(readFileSync(documentPath, 'utf8')),
    authorizationByOperation,
  );
  assertAuthorizationCoverage(document, authorizationByOperation);
  const groups = groupByTag(document);

  rmSync(outputDirectory, { recursive: true, force: true });
  mkdirSync(outputDirectory, { recursive: true });
  mkdirSync(publicDirectory, { recursive: true });
  writeFileSync(join(publicDirectory, 'agentprism.json'), `${JSON.stringify(document, null, 2)}\n`);

  for (const [tag, operations] of groups) {
    writeFileSync(
      join(outputDirectory, `${slugify(tag)}.md`),
      renderGroup(tag, operations, document, authorizationByOperation),
    );
  }

  const schemas = Object.entries(document.components?.schemas ?? {}).sort((left, right) =>
    left[0].localeCompare(right[0]),
  );
  const schemaSlugs = new Set();
  for (const [name, schema] of schemas) {
    const slug = slugify(name);
    if (schemaSlugs.has(slug)) {
      throw new Error(`OpenAPI schema slug collision: ${name} → ${slug}`);
    }
    schemaSlugs.add(slug);
    writeFileSync(join(outputDirectory, `schema-${slug}.md`), renderSchema(name, schema));
  }
  writeFileSync(join(outputDirectory, 'schemas.md'), renderSchemaIndex(schemas));

  mkdirSync(dirname(sidebarFile), { recursive: true });
  writeFileSync(
    sidebarFile,
    `${JSON.stringify(
      [
        ...[...groups.entries()].map(([tag, operations]) => ({
          label: `${tag} (${operations.length})`,
          link: `${sidebarBase}/${slugify(tag)}/`,
        })),
        {
          label: `Schemas (${Object.keys(document.components?.schemas ?? {}).length})`,
          link: `${sidebarBase}/schemas/`,
        },
      ],
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

function renderGroup(tag, operations, document, authorizationByOperation) {
  const lines = [
    '---',
    `title: ${quote(tag)}`,
    `description: ${quote(`The ${tag.toLowerCase()} endpoints of the AgentPrism HTTP API — ${operations.length} operations.`)}`,
    `slug: http-api/${slugify(tag)}`,
    'editUrl: false',
    'lastUpdated: false',
    '---',
    '',
    `${operations.length} operations. \`{prefix}\` is the route prefix passed to`,
    '`MapAgentPrism`; the template uses `/agentprism`.',
    '',
  ];

  if (tag === 'OpenAI') {
    lines.push(
      'The OpenAPI snapshot cannot express the polymorphic request bodies of the Responses',
      'and Chat Completions adapters. Use the [OpenAI-compatible API guide]',
      '(/AgentPrism/guides/openai-api/) for copyable requests, streaming events, state,',
      'and the exact compatibility boundary.',
      '',
    );
  }

  for (const { path, method, operation } of operations) {
    lines.push(`## ${method.toUpperCase()} ${renderPath(path)}`);
    lines.push('');

    lines.push(`**Operation ID:** \`${operation.operationId ?? 'not specified'}\``);
    lines.push('');

    if (operation.summary) {
      lines.push(`**${operation.summary}**`);
      lines.push('');
    }

    if (operation.description) {
      lines.push(sanitizeInternalHistory(operation.description));
      lines.push('');
    }

    const security = operation.security ?? document.security ?? [];
    const authorization = authorizationByOperation.get(operation.operationId);
    if (security.length === 0) {
      lines.push('**Authorization:** anonymous.');
    } else if (authorization) {
      const role = authorization.role
        ? `\`${authorization.role}\` role policy when that policy is registered`
        : 'no endpoint-specific role policy';
      const scope = authorization.scope
        ? `\`${authorization.scope}\` API-key scope`
        : 'no endpoint-specific API-key scope';
      lines.push(`**Authorization:** bearer authentication; ${role}; ${scope}.`);
    } else {
      lines.push(
        '**Authorization:** bearer authentication. See the [role and scope model]' +
          '(/AgentPrism/getting-started/security/).',
      );
    }
    lines.push('');

    const parameters = operation.parameters ?? [];

    if (parameters.length > 0) {
      lines.push('| Parameter | In | Required | Type | Description and rules |');
      lines.push('|---|---|---|---|---|');

      for (const parameter of parameters) {
        const detail = [parameter.description, schemaRules(parameter.schema)]
          .filter(Boolean)
          .map(sanitizeInternalHistory)
          .join(' ');
        lines.push(
          `| \`${parameter.name}\` | ${parameter.in} | ${parameter.required ? 'yes' : 'no'} | ` +
            `${describeSchema(parameter.schema)} | ${escapeTable(detail) || '—'} |`,
        );
      }

      lines.push('');
    }

    const requestBody = operation.requestBody;

    if (requestBody) {
      const content = Object.entries(requestBody.content ?? {});

      if (content.length > 0) {
        lines.push(`**Request body** (${requestBody.required ? 'required' : 'optional'}):`);
        lines.push('');
        for (const [mediaType, media] of content) {
          lines.push(`- \`${mediaType}\` → ${describeSchema(media?.schema)}`);
        }
        lines.push('');

        const example = content.map(([, media]) => media?.example).find((value) => value !== undefined);
        if (example !== undefined) {
          lines.push('```json');
          lines.push(JSON.stringify(example, null, 2));
          lines.push('```');
          lines.push('');
        }
      }
    }

    lines.push('| Response | Body | Headers |');
    lines.push('|---|---|---|');

    for (const [status, response] of Object.entries(operation.responses ?? {})) {
      const content = Object.entries(response.content ?? {});
      const body =
        content.length > 0
          ? content
              .map(([mediaType, media]) => `\`${mediaType}\` → ${describeSchema(media?.schema)}`)
              .join('<br>')
          : '—';
      const headers = Object.entries(response.headers ?? {})
        .map(([name, header]) => `\`${name}\` ${describeSchema(header.schema)}`)
        .join('<br>');
      lines.push(
        `| **${status}** ${escapeTable(sanitizeInternalHistory(response.description ?? ''))} | ${body} | ${headers || '—'} |`,
      );
    }

    lines.push('');
  }

  return lines.join('\n');
}

/** A readable one-liner for a schema: the type name if it has one, the shape if not. */
function describeSchema(schema) {
  if (!schema) {
    return '—';
  }

  if (schema.$ref) {
    const name = schema.$ref.split('/').pop();
    return `[\`${name}\`](${base}/schemas/${slugify(name)}/)`;
  }

  if (schema.type === 'array') {
    return `array of ${describeSchema(schema.items)}`;
  }

  for (const keyword of ['oneOf', 'anyOf', 'allOf']) {
    if (Array.isArray(schema[keyword])) {
      return schema[keyword].map((entry) => describeSchema(entry)).join(` ${keyword} `);
    }
  }

  if (Array.isArray(schema.enum)) {
    return `enum: ${schema.enum.map((value) => `\`${value}\``).join(', ')}`;
  }

  const type = Array.isArray(schema.type) ? schema.type.filter((entry) => entry !== 'null')[0] : schema.type;

  return type ? `\`${type}${schema.format ? ` (${schema.format})` : ''}\`` : '`object`';
}

function renderSchemaIndex(schemas) {
  const lines = [
    '---',
    'title: HTTP schemas',
    `description: ${quote(`Request and response shapes for all ${schemas.length} schemas in the generated AgentPrism OpenAPI contract.`)}`,
    'slug: http-api/schemas',
    'editUrl: false',
    'lastUpdated: false',
    '---',
    '',
    `${schemas.length} schemas generated from the published OpenAPI snapshot. Required fields,`,
    'validation constraints, defaults, and nested references are shown on each schema page.',
    '',
  ];

  let currentInitial;
  for (const [name] of schemas) {
    const initial = /^[a-z]/i.test(name) ? name[0].toUpperCase() : '#';
    if (initial !== currentInitial) {
      currentInitial = initial;
      lines.push(`## ${initial}`);
      lines.push('');
    }
    lines.push(`- [\`${name}\`](${base}/schemas/${slugify(name)}/)`);
  }

  lines.push('');
  return lines.join('\n');
}

function renderSchema(name, schema) {
  const lines = [
    '---',
    `title: ${quote(name)}`,
    `description: ${quote(`OpenAPI request or response schema for ${name}, including required fields, defaults, constraints, and linked nested types.`)}`,
    `slug: http-api/schemas/${slugify(name)}`,
    'editUrl: false',
    'lastUpdated: false',
    '---',
    '',
    '[← All HTTP schemas](/AgentPrism/http-api/schemas/)',
    '',
  ];

  if (schema.description) {
    lines.push(sanitizeInternalHistory(schema.description));
    lines.push('');
  }

  const shape = describeSchema({ ...schema, $ref: undefined });
  lines.push(`**Shape:** ${shape}`);
  lines.push('');

  if (Array.isArray(schema.enum)) {
    lines.push(`**Allowed values:** ${schema.enum.map((value) => `\`${value}\``).join(', ')}`);
    lines.push('');
  }

  const properties = Object.entries(schema.properties ?? {});
  const required = new Set(schema.required ?? []);
  if (properties.length > 0) {
    lines.push('| Property | Required | Type | Description | Rules |');
    lines.push('|---|---|---|---|---|');
    for (const [propertyName, property] of properties) {
      lines.push(
        `| \`${propertyName}\` | ${required.has(propertyName) ? 'yes' : 'no'} | ${describeSchema(property)} | ` +
          `${escapeTable(sanitizeInternalHistory(property.description ?? '')) || '—'} | ${escapeTable(schemaRules(property)) || '—'} |`,
      );
    }
    lines.push('');
  }

  const rules = schemaRules(schema);
  if (rules) {
    lines.push(`**Constraints:** ${rules}`);
    lines.push('');
  }

  return lines.join('\n');
}

function schemaRules(schema = {}) {
  const rules = [];
  if (schema.default !== undefined) rules.push(`default \`${formatValue(schema.default)}\``);
  if (schema.minimum !== undefined) rules.push(`minimum ${schema.minimum}`);
  if (schema.maximum !== undefined) rules.push(`maximum ${schema.maximum}`);
  if (schema.minLength !== undefined) rules.push(`minimum length ${schema.minLength}`);
  if (schema.maxLength !== undefined) rules.push(`maximum length ${schema.maxLength}`);
  if (schema.minItems !== undefined) rules.push(`minimum items ${schema.minItems}`);
  if (schema.maxItems !== undefined) rules.push(`maximum items ${schema.maxItems}`);
  if (schema.pattern) rules.push(`pattern \`${schema.pattern.replaceAll('|', '\\|')}\``);
  if (schema.uniqueItems) rules.push('items must be unique');
  if (schema.readOnly) rules.push('read-only');
  if (schema.writeOnly) rules.push('write-only');
  return rules.join('; ');
}

function readEndpointAuthorization() {
  const result = new Map();
  const sources = collectFiles(endpointSourceDirectory).filter((file) => file.endsWith('.cs'));
  const chainPattern = /\b\w+\.Map(?:Get|Post|Put|Patch|Delete)\([\s\S]*?\.WithName\("([^"]+)"\)/g;

  for (const file of sources) {
    const source = readFileSync(file, 'utf8');
    for (const match of source.matchAll(chainPattern)) {
      const chain = match[0];
      const operationId = match[1];
      const role = /\.RequireRole\(roles\.(Reader|Operator|Admin)\)/.exec(chain)?.[1];
      const scope = /\.RequireApiKeyScope\(ApiKeyScope\.(\w+)\)/.exec(chain)?.[1];
      const anonymous = /\.AllowAnonymous\(\)/.test(chain) ||
        operationId === 'AgentPrismMcpOAuthCallback' ||
        operationId === 'AgentPrismAcceptInboundTrigger';

      result.set(operationId, { role, scope, anonymous });
    }
  }

  return result;
}

function collectFiles(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? collectFiles(path) : [path];
  });
}

function assertAuthorizationCoverage(document, authorizationByOperation) {
  const missingRoles = [];
  const missingScopes = [];

  for (const item of Object.values(document.paths ?? {})) {
    for (const method of methods) {
      const operation = item[method];
      if (!operation || operation.security?.length === 0) continue;
      const authorization = authorizationByOperation.get(operation.operationId);
      if (!authorization?.role) missingRoles.push(operation.operationId);
      if (!authorization?.scope && operation.operationId !== 'AgentPrismCurrentTenant') {
        missingScopes.push(operation.operationId);
      }
    }
  }

  if (missingRoles.length > 0 || missingScopes.length > 0) {
    throw new Error(
      `HTTP authorization metadata is incomplete. Missing roles: ${missingRoles.join(', ') || 'none'}; ` +
        `missing scopes: ${missingScopes.join(', ') || 'none'}.`,
    );
  }
}

function normalizeDocument(document, authorizationByOperation) {
  const normalized = structuredClone(document);
  sanitizeDescriptionFields(normalized);
  const bearer = normalized.components?.securitySchemes?.bearer;
  if (bearer?.description) {
    bearer.description = bearer.description.replace('/api/keys', '/api/api-keys');
  }

  for (const item of Object.values(normalized.paths ?? {})) {
    for (const method of methods) {
      const operation = item[method];
      if (!operation) continue;
      const authorization = authorizationByOperation.get(operation.operationId);
      if (authorization?.anonymous) {
        operation.security = [];
      }
      if (authorization?.role) {
        operation['x-agentprism-role'] = authorization.role;
      }
      if (authorization?.scope) {
        operation['x-agentprism-api-key-scope'] = authorization.scope;
      }
    }
  }

  return normalized;
}

function sanitizeDescriptionFields(value) {
  if (Array.isArray(value)) {
    value.forEach(sanitizeDescriptionFields);
    return;
  }

  if (!value || typeof value !== 'object') {
    return;
  }

  for (const [key, child] of Object.entries(value)) {
    if ((key === 'description' || key === 'summary') && typeof child === 'string') {
      value[key] = sanitizeInternalHistory(child);
    } else {
      sanitizeDescriptionFields(child);
    }
  }
}

function renderPath(path) {
  return path.startsWith('/agentprism') ? `{prefix}${path.slice('/agentprism'.length)}` : `{prefix}${path}`;
}

function sanitizeInternalHistory(text) {
  return String(text)
    .replace(/\b(?:[A-Za-z_][A-Za-z0-9_.<>]*|[A-Za-z_][A-Za-z0-9_]*\[\])\?\s+(?=[A-Z][A-Za-z0-9_.]+\b)/g, '')
    .replace(/This follows the\s+same rationale as RunStatistics\.ErrorRate\./gi, 'It uses the same calculation as run statistics.')
    .replace(/\b(?:design\s+)?rule\s+K1\b|\bK1\b/gi, 'the safe-default rule')
    .replace(/\b(?:design\s+)?rule\s+K2\b|\bK2\b/gi, 'the code-only tool rule')
    .replace(/\b(?:design\s+)?rule\s+K3\b|\bK3\b/gi, 'the MAF pass-through rule')
    .replace(/\b(?:design\s+)?rule\s+K4\b|\bK4\b/gi, 'the replaceable-extension rule')
    .replace(/\s*[—–-]?\s*\(?\b(?:phase|faz)\s+\d+\b\)?/gi, '')
    .replace(/\s*\(?\b(?:K|F)-\d{2,3}\b\)?/gi, '')
    .replace(/\s*\(?\b(?:HATA|MT)-[A-Z0-9-]+\b\)?/g, '')
    .replace(/(?:Reason:\s*)?`?docs\/(?:KARARLAR\.md|[^`\s),]+)`?,?/gi, '')
    .replace(/\s*\(?\b(?:see\s+)?section\s+\d+(?:\.\d+)?(?:\/[A-Z0-9-]+)?\b\)?[,]?/gi, '')
    .replace(/\bRationale:\s*(?:and\s+)?(?:of\s*)?\./gi, '')
    .replace(/\bSee\s*,?\s*for the rationale\.?/gi, '')
    .replace(/\bThe the\b/g, 'The')
    .replace(/\bthe the\b/g, 'the')
    .replace(/\ba deliberate the\b/g, 'a deliberate')
    .replace(/\ba the\b/g, 'the')
    .replace(/\b(?:decision|rationale) the (safe-default|code-only tool|MAF pass-through|replaceable-extension) rule\b/gi, 'the $1 rule')
    .replace(/\bRationale:\s*the code-only tool rule\s*[-—]\s*/gi, 'The code-only tool rule states: ')
    .replace(/\brationale\b/gi, 'reason')
    .replace(/\b(?:OLDEST|AS IS)\b/g, (value) => value.toLowerCase())
    .replace(/\s+([,.;:])/g, '$1')
    .replace(/\(\s*\)/g, '')
    .trim();
}

function formatValue(value) {
  return typeof value === 'string' ? value : JSON.stringify(value);
}

function escapeTable(text) {
  return String(text).replaceAll('|', '\\|').replace(/\s+/g, ' ').trim();
}

function slugify(text) {
  return text.toLowerCase().replaceAll(/[^a-z0-9]+/g, '-').replaceAll(/^-|-$/g, '');
}

function quote(text) {
  return `"${text.replaceAll('\\', '\\\\').replaceAll('"', '\\"')}"`;
}
