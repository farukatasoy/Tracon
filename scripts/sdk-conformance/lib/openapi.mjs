// Reads an OpenAPI document the way a client generator's consumer does: lists its operations, builds the smallest
// request each one accepts, and checks an answer against the schema the document declares for its status.

import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';

const METHODS = ['get', 'post', 'put', 'delete', 'patch'];
const DOCUMENT_ID = 'urn:tracon:document';

/** Every operation of the document: method, path template, id, parameters, body and the statuses it lists. */
export function listOperations(document) {
  const operations = [];

  for (const [path, item] of Object.entries(document.paths)) {
    for (const method of METHODS) {
      const operation = item[method];

      if (!operation) {
        continue;
      }

      const content = operation.requestBody?.content ?? {};
      const [mediaType] = Object.keys(content);

      operations.push({
        operationId: operation.operationId,
        method,
        path,
        parameters: [...(item.parameters ?? []), ...(operation.parameters ?? [])],
        requestMediaType: mediaType ?? null,
        requestSchema: mediaType ? (content[mediaType].schema ?? {}) : null,
        responses: operation.responses ?? {},
      });
    }
  }

  return operations;
}

/** Follows a `$ref` into `components.schemas`, one hop at a time, until a schema that is not a reference. */
export function resolve(schema, document) {
  let current = schema;

  while (current && typeof current === 'object' && typeof current.$ref === 'string') {
    current = document.components.schemas[current.$ref.slice(current.$ref.lastIndexOf('/') + 1)];
  }

  return current;
}

/** A validator for the schemas of one document. A schema's `$ref` points back into the same document. */
export function createValidator(document) {
  const ajv = new Ajv2020({ strict: false, allErrors: false, validateFormats: true });

  addFormats(ajv);

  // OpenAPI formats that JSON Schema does not define. They describe the CLR type, not the text of the value.
  ajv.addFormat('int32', { type: 'number', validate: (value) => Number.isInteger(value) && Math.abs(value) <= 2 ** 31 });
  ajv.addFormat('int64', { type: 'number', validate: (value) => Number.isInteger(value) });

  for (const name of ['double', 'float', 'byte', 'binary']) {
    ajv.addFormat(name, true);
  }

  ajv.addSchema({ $id: DOCUMENT_ID, components: rewriteReferences(document.components) });

  const compiled = new Map();

  return {
    /** Returns the validation errors as one line, or `null` when the value is a valid instance of the schema. */
    check(schema, value) {
      let validate = compiled.get(schema);

      if (!validate) {
        validate = ajv.compile(rewriteReferences(schema));
        compiled.set(schema, validate);
      }

      return validate(value)
        ? null
        : validate.errors.map((error) => `${error.instancePath || '/'} ${error.message}`).slice(0, 3).join('; ');
    },
  };
}

function rewriteReferences(node) {
  if (Array.isArray(node)) {
    return node.map(rewriteReferences);
  }

  if (node && typeof node === 'object') {
    return Object.fromEntries(
      Object.entries(node).map(([key, value]) => [
        key,
        key === '$ref' && typeof value === 'string' && value.startsWith('#/') ? DOCUMENT_ID + value : rewriteReferences(value),
      ]),
    );
  }

  return node;
}

const FIXTURE_TEXT = 'sample';
const FIXTURE_GUID = '11111111-1111-1111-1111-111111111111';

/** The smallest value the schema accepts: required members only, first enum member, one for every number. */
export function sampleFor(schema, document, depth = 0) {
  if (depth > 6) {
    return undefined;
  }

  const resolved = resolve(schema, document);

  if (!resolved || typeof resolved !== 'object') {
    return undefined;
  }

  if (Array.isArray(resolved.enum) && resolved.enum.length > 0) {
    return resolved.enum[0];
  }

  for (const key of ['oneOf', 'anyOf']) {
    if (Array.isArray(resolved[key])) {
      const branch = resolved[key].find((candidate) => candidate.type !== 'null');

      return branch === undefined ? undefined : sampleFor(branch, document, depth + 1);
    }
  }

  if (Array.isArray(resolved.allOf)) {
    return Object.assign({}, ...resolved.allOf.map((part) => sampleFor(part, document, depth + 1) ?? {}));
  }

  const kind = Array.isArray(resolved.type) ? (resolved.type.find((t) => t !== 'null') ?? 'object') : (resolved.type ?? 'object');

  switch (kind) {
    case 'string':
      return stringFor(resolved);
    case 'integer':
      return Math.max(1, resolved.minimum ?? 1);
    case 'number':
      return 1;
    case 'boolean':
      return false;
    case 'array':
      return [];
    default: {
      const result = {};

      for (const name of resolved.required ?? []) {
        const value = sampleFor(resolved.properties?.[name] ?? {}, document, depth + 1);

        if (value !== undefined) {
          result[name] = value;
        }
      }

      return result;
    }
  }
}

function stringFor(schema) {
  // A time span is a string with a pattern; the fixture text is not one.
  if (typeof schema.pattern === 'string' && schema.pattern.includes('\\d{2}:\\d{2}:\\d{2}')) {
    return '00:00:30';
  }

  switch (schema.format) {
    case 'uuid':
      return FIXTURE_GUID;
    case 'date-time':
      return '2026-01-01T00:00:00+00:00';
    case 'date':
      return '2026-01-01';
    case 'uri':
      return 'https://example.test/';
    case 'byte':
      return 'c2FtcGxl';
    default:
      return FIXTURE_TEXT;
  }
}

export { FIXTURE_GUID, FIXTURE_TEXT };
