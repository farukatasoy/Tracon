// Calls every operation of the live OpenAPI document through @tracon/client with the smallest request the document
// accepts, and checks every answer against the schema the document declares for its status.
//
// The generated types are erased at run time and openapi-fetch does not check a body it reads, so the schemas of the
// document stand in for the types: a field the type calls a string and the server sends as a number is a mismatch
// here, and nowhere else. The same sweep exists for the .NET client, whose deserializer does that check by itself.

import { createTraconClient } from '../../../packages/tracon-client/dist/index.js';
import { createValidator, FIXTURE_GUID, FIXTURE_TEXT, listOperations, resolve, sampleFor } from '../lib/openapi.mjs';

const AGENT = 'summarizer';
const SESSION = 'roundtrip-session';
const CALL_TIMEOUT_MS = 30_000;

// The bodies the document describes as "any JSON": the operation needs a real request to get past its first check.
const SAMPLE_AGENT = { name: 'sample', instructions: 'Answer briefly.', model: { provider: 'echo', model: 'echo-1' } };

const BODY_OVERRIDES = {
  TraconRunAgent: { message: 'sweep run', sessionId: SESSION },
  TraconCreateAgent: SAMPLE_AGENT,
  TraconUpdateAgent: SAMPLE_AGENT,
  TraconValidateAgent: SAMPLE_AGENT,
  TraconAGUIRun: { threadId: SESSION, runId: 'sweep-agui-run', messages: [{ id: 'sweep-1', role: 'user', content: 'sweep' }] },
  TraconOpenAIResponses: { model: AGENT, input: 'sweep' },
  TraconOpenAIChatCompletions: { model: AGENT, messages: [{ role: 'user', content: 'sweep' }] },
};

// Operations that fail the sweep and are tolerated, each with the reason. The list only shrinks. Each one is a probe that
// fails today and is written up in client-roundtrip.finding.
export const KNOWN_OPEN = new Map([
  ['TraconA2A_summarizer', 'answers a JSON-RPC body; the document lists no content for its 200'],
  ['TraconA2AAgentCard_summarizer', 'answers the agent card as JSON; the document lists no content for its 200'],
  ['TraconMcpOAuthCallback', 'answers an HTML page; the document lists no content for its 200'],
]);

/** The number of operations that must read back as a typed value; it grows when the fixtures improve, never shrinks. */
export const MINIMUM_RETURNED = 85;

export const OUTCOME = {
  returned: 'returned',
  documentedStatus: 'documented-status',
  undocumentedStatus: 'undocumented-status',
  schemaMismatch: 'schema-mismatch',
  contentTypeMismatch: 'content-type-mismatch',
  clientFault: 'client-fault',
  timeout: 'timeout',
};

export function isProblem(outcome) {
  switch (outcome.kind) {
    case OUTCOME.schemaMismatch:
    case OUTCOME.contentTypeMismatch:
    case OUTCOME.clientFault:
    case OUTCOME.timeout:
      return true;
    case OUTCOME.undocumentedStatus:
      // A 4xx the document leaves out is a documentation gap that the OpenAPI conformance run reports. A success the
      // document does not list, or a server fault, is a fault of the contract.
      return outcome.status < 400 || outcome.status >= 500;
    case OUTCOME.documentedStatus:
      return outcome.status >= 500 && ![501, 502, 503].includes(outcome.status);
    default:
      return false;
  }
}

function normalize(name) {
  return name.replace(/[^a-z0-9]/giu, '').toLowerCase();
}

function pathValue(parameter, operation, document, fixtures) {
  const name = normalize(parameter.name);
  const schema = resolve(parameter.schema ?? {}, document);

  if (schema.format === 'uuid') {
    return ['runid', 'id', 'a', 'b', 'scoreid', 'ruleid'].includes(name) ? fixtures.runId : FIXTURE_GUID;
  }

  if (schema.type === 'integer' || schema.type === 'number') {
    return 1;
  }

  switch (name) {
    case 'sessionid':
      return SESSION;
    // Not `default`: saving a tenant under that name changes what every later call of the sweep is allowed to do.
    case 'tenantid':
    case 'slug':
      return 'sweep-tenant';
    case 'name':
    case 'skillname':
      // The agent the sweep saves has to be called by the name it was saved under.
      return operation.operationId === 'TraconUpdateAgent' ? 'sample' : operation.path.includes('/agents') ? AGENT : FIXTURE_TEXT;
    case 'id':
      return fixtures.runId;
    default:
      return Array.isArray(schema.enum) ? schema.enum[0] : FIXTURE_TEXT;
  }
}

function buildInit(operation, document, fixtures) {
  const path = {};
  const query = {};

  for (const parameter of operation.parameters) {
    if (parameter.in === 'path') {
      path[parameter.name] = pathValue(parameter, operation, document, fixtures);
    } else if (parameter.in === 'query' && parameter.required) {
      query[parameter.name] = sampleFor(parameter.schema ?? {}, document) ?? FIXTURE_TEXT;
    }
  }

  const init = { params: { path, query }, parseAs: 'text', signal: AbortSignal.timeout(CALL_TIMEOUT_MS) };

  if (operation.requestMediaType === 'application/json') {
    init.body = BODY_OVERRIDES[operation.operationId] ?? sampleFor(operation.requestSchema, document) ?? {};
  } else if (operation.requestMediaType === 'multipart/form-data') {
    const form = new FormData();

    form.append('file', new Blob(['sample'], { type: 'text/plain' }), 'sample.txt');
    init.body = {};
    init.bodySerializer = () => form;
  }

  return init;
}

/** The status key the document uses for an answer: the exact code, the class (`2XX`) or `default`. */
function declaredResponse(operation, status) {
  return operation.responses[String(status)] ?? operation.responses[`${String(status)[0]}XX`] ?? operation.responses.default ?? null;
}

function mediaTypeOf(contentType) {
  return (contentType ?? '').split(';')[0].trim().toLowerCase();
}

function describe(operation, kind, status, detail) {
  return { operationId: operation.operationId, method: operation.method.toUpperCase(), path: operation.path, kind, status, detail };
}

/** Compares one answer to the document: its status, its media type and its body. */
function judge(operation, raw, validator) {
  const declared = declaredResponse(operation, raw.status);
  const success = raw.status >= 200 && raw.status < 300;

  if (declared === null) {
    return describe(operation, OUTCOME.undocumentedStatus, raw.status, raw.text.slice(0, 300));
  }

  const content = declared.content ?? {};
  const listed = Object.keys(content);
  const actual = mediaTypeOf(raw.contentType);

  if (raw.text.length > 0 && listed.length > 0 && !listed.some((media) => media.toLowerCase() === actual)) {
    return describe(operation, OUTCOME.contentTypeMismatch, raw.status, `answered ${actual || '(none)'}, the document lists ${listed.join(', ')}`);
  }

  if (raw.text.length > 0 && listed.length === 0) {
    return describe(operation, OUTCOME.contentTypeMismatch, raw.status, `answered a body (${actual || 'no type'}), the document lists none`);
  }

  if (actual.endsWith('json') && raw.text.length > 0) {
    const schema = content[listed.find((media) => media.toLowerCase() === actual)]?.schema;
    let body;

    try {
      body = JSON.parse(raw.text);
    } catch {
      return describe(operation, OUTCOME.clientFault, raw.status, `the body is not JSON: ${raw.text.slice(0, 120)}`);
    }

    const problem = schema ? validator.check(schema, body) : null;

    if (problem !== null) {
      return describe(operation, OUTCOME.schemaMismatch, raw.status, problem);
    }
  }

  return describe(operation, success ? OUTCOME.returned : OUTCOME.documentedStatus, raw.status, success ? null : raw.text.slice(0, 300));
}

/** Calls one operation and returns how it ended. */
async function callOperation(client, capture, operation, document, validator, fixtures) {
  capture.last = null;

  let init;

  try {
    init = buildInit(operation, document, fixtures);
  } catch (error) {
    return describe(operation, OUTCOME.clientFault, null, `no sample: ${error.message}`);
  }

  try {
    await client[operation.method.toUpperCase()](operation.path.replace(/^\/tracon/u, ''), init);
  } catch (error) {
    if (error?.name === 'TimeoutError' || error?.name === 'AbortError') {
      return describe(operation, OUTCOME.timeout, null, 'the call did not finish in time');
    }

    // A failed status is thrown by the client layer after the answer was read; the answer is what is judged.
    if (!(error?.name === 'TraconError' && capture.last)) {
      return describe(operation, OUTCOME.clientFault, capture.last?.status ?? null, `${error?.name}: ${String(error?.message).slice(0, 300)}`);
    }
  }

  return capture.last === null
    ? describe(operation, OUTCOME.clientFault, null, 'no answer was recorded')
    : judge(operation, capture.last, validator);
}

/** A client whose fetch records every answer, with the demo role the example host reads. */
function createRecordingClient(baseUrl, mutate) {
  const capture = { last: null };
  const client = createTraconClient({
    baseUrl: `${baseUrl}/tracon`,
    fetch: async (request) => {
      request.headers.set('X-Tracon-Demo-Role', 'admin');

      let response = await globalThis.fetch(request);
      const contentType = response.headers.get('content-type');
      const text = await response.clone().text();
      const changed = mutate ? mutate(request, response, text) : null;

      if (changed) {
        response = new Response(changed.text ?? text, { status: changed.status ?? response.status, headers: changed.headers ?? response.headers });
      }

      capture.last = { status: response.status, contentType: changed?.headers?.get('content-type') ?? contentType, text: changed?.text ?? text };

      return response;
    },
  });

  return { client, capture };
}

function rank(operation) {
  if (operation.operationId.includes('RunAgent')) {
    return 0;
  }

  return { put: 1, post: 2, get: 3, patch: 4 }[operation.method] ?? 5;
}

/** Runs the sweep and returns one outcome per operation. */
export async function sweep(baseUrl, document, mutate) {
  const validator = createValidator(document);
  const { client, capture } = createRecordingClient(baseUrl, mutate);
  const operations = listOperations(document).sort((a, b) => rank(a) - rank(b));
  const fixtures = { runId: FIXTURE_GUID };
  const outcomes = [];

  for (const operation of operations) {
    const outcome = await callOperation(client, capture, operation, document, validator, fixtures);

    if (operation.operationId === 'TraconRunAgent' && capture.last) {
      fixtures.runId = /"runId":"([0-9a-f-]{36})"/u.exec(capture.last.text)?.[1] ?? fixtures.runId;
    }

    outcomes.push(outcome);
  }

  return outcomes;
}

/** The gate must be able to go red: an answer rewritten to the wrong type, and one with the wrong media type. */
async function selfTest(baseUrl, document) {
  const wrongType = await sweep(baseUrl, document, (request, _response, text) =>
    request.method === 'GET' && new URL(request.url).pathname === '/tracon/api/agents'
      ? { text: text.replace(/"name":"summarizer"/u, '"name":42') }
      : null,
  );
  const wrongMedia = await sweep(baseUrl, document, (request, response) =>
    request.method === 'GET' && new URL(request.url).pathname === '/tracon/api/agents'
      ? { headers: new Headers({ 'content-type': 'text/plain' }) }
      : null,
  );
  const wrongStatus = await sweep(baseUrl, document, (request) =>
    request.method === 'GET' && new URL(request.url).pathname === '/tracon/api/agents' ? { status: 202 } : null,
  );
  const results = [];

  const status = wrongStatus.find((outcome) => outcome.operationId === 'TraconListAgents');

  results.push({
    id: 'self-test: a success status the document does not list is a problem',
    ok: status?.kind === OUTCOME.undocumentedStatus && isProblem(status),
    detail: `${status?.kind} ${status?.status}`,
  });

  const mismatch = wrongType.find((outcome) => outcome.operationId === 'TraconListAgents');

  results.push({
    id: 'self-test: a field of the wrong type is a schema mismatch',
    ok: mismatch?.kind === OUTCOME.schemaMismatch,
    detail: `${mismatch?.kind}: ${mismatch?.detail}`,
  });

  const media = wrongMedia.find((outcome) => outcome.operationId === 'TraconListAgents');

  results.push({
    id: 'self-test: a wrong media type is a content-type mismatch',
    ok: media?.kind === OUTCOME.contentTypeMismatch,
    detail: `${media?.kind}: ${media?.detail}`,
  });

  return results;
}

export async function run({ baseUrl, document }) {
  const results = await selfTest(baseUrl, document);
  const outcomes = await sweep(baseUrl, document);
  const operations = listOperations(document);

  results.push({
    id: 'one outcome per operation of the live document',
    ok: outcomes.length === operations.length && new Set(outcomes.map((o) => o.operationId)).size === operations.length,
    detail: `${outcomes.length} outcomes, ${operations.length} operations`,
  });

  const problems = outcomes.filter(isProblem);
  const unexpected = problems.filter((outcome) => !KNOWN_OPEN.has(outcome.operationId));

  for (const outcome of unexpected) {
    results.push({
      id: `${outcome.kind} ${outcome.method} ${outcome.path} (${outcome.operationId})`,
      ok: false,
      detail: `status=${outcome.status} ${outcome.detail ?? ''}`,
    });
  }

  const stale = [...KNOWN_OPEN.keys()].filter((id) => !problems.some((outcome) => outcome.operationId === id));

  results.push({ id: 'the known-open list holds only operations that still fail', ok: stale.length === 0, detail: stale.join(', ') });

  const returned = outcomes.filter((outcome) => outcome.kind === OUTCOME.returned).length;

  results.push({
    id: `at least ${MINIMUM_RETURNED} operations read back as a typed value`,
    ok: returned >= MINIMUM_RETURNED,
    detail: `${returned} of ${outcomes.length}`,
  });

  const byKind = {};

  for (const outcome of outcomes) {
    byKind[outcome.kind] = (byKind[outcome.kind] ?? 0) + 1;
  }

  return { results, outcomes, summary: byKind };
}
