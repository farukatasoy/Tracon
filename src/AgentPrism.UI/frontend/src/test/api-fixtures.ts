import { vi } from 'vitest';
import type { AgentPrismMetaResponse } from '@agentprism/client';
import { matchRoute } from '../lib/router';

/** A fully-permissioned, persistent instance — the shape most screens assume when nothing is being tested against a specific role or storage mode. */
export const testMeta: AgentPrismMetaResponse = {
  version: 'test',
  prefix: '/agentprism',
  authentication: { requiresBearerToken: false, allowRemoteAccess: true, requiresAuthorizationPolicy: false },
  storage: {
    persistent: true,
    agentDefinitionStore: 'Test',
    runStore: 'Test',
    sessionStore: 'Test',
    jobStore: 'Test',
    jobWorkerEnabled: true,
  },
  roles: { canRead: true, canOperate: true, canAdminister: true },
};

/** A raw `Response` bypasses JSON encoding entirely — see `sseFixture`. */
export type FixtureHandler = (params: Record<string, string>, url: URL) => unknown | Response;

export interface FixtureRoute {
  method: 'GET' | 'POST' | 'PUT' | 'DELETE' | 'PATCH';
  /** A `lib/router`-style pattern, without a leading slash, e.g. `api/agents/:name`. */
  pattern: string;
  handler: FixtureHandler;
  /** HTTP status for this route's response. Defaults to 200. Ignored when the handler returns a `Response`. */
  status?: number;
}

/** Shorthand for a fixture that always returns the same JSON body. */
export function fixture(method: FixtureRoute['method'], pattern: string, body: unknown, status = 200): FixtureRoute {
  return { method, pattern, handler: () => body, status };
}

/**
 * A `text/event-stream` `Response` carrying `text` verbatim — for
 * `POST api/agents/{name}/run`, the one endpoint the playground reads through
 * `openStream`/`readSse` instead of the typed `client`. Build `text` with
 * `sseFrame()` below; use this directly in a custom `FixtureRoute.handler`
 * that varies its script across calls (e.g. an approval on the first run, a
 * plain reply on the follow-up decision run).
 */
export function sseResponse(text: string): Response {
  const stream = new ReadableStream<Uint8Array>({
    start(controller) {
      controller.enqueue(new TextEncoder().encode(text));
      controller.close();
    },
  });

  return new Response(stream, { status: 200, headers: { 'Content-Type': 'text/event-stream' } });
}

/** Shorthand for a fixture that always replays the same SSE script. */
export function sseFixture(method: FixtureRoute['method'], pattern: string, text: string): FixtureRoute {
  return { method, pattern, handler: () => sseResponse(text) };
}

/** One SSE frame in the shape `readSse` (`@agentprism/client`) expects. */
export function sseFrame(event: string, data: unknown): string {
  return `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
}

/**
 * Endpoints whose success shape is a bare array. A route not listed here (and
 * not covered by an explicit override) gets `{}` instead — the safer default
 * for a single-resource GET, most of which are read through optional
 * chaining (`data?.definition?.…`) and tolerate a plain empty object.
 */
const DEFAULT_COLLECTION_ROUTES = [
  'api/agents',
  'api/tools',
  'api/tools/usage',
  'api/skills',
  'api/skill-script-grants',
  'api/models',
  'api/models/health',
  'api/runs',
  'api/runs/:runId/tools',
  'api/jobs',
  'api/sessions',
  'api/workflows',
  'api/evals',
  'api/evals/:name/cases',
  'api/evals/:name/runs',
  'api/experiments',
  'api/triggers',
  'api/webhooks',
  'api/webhooks/:name/deliveries',
  'api/api-keys',
  'api/mcp-servers',
  'api/mcp-servers/:name/prompts',
  'api/mcp-servers/:name/resources',
  'api/approvals/pending',
  'api/approvals/rules',
  'api/audit',
  'api/tenants/:tenantId/providers',
  'api/voice/voices',
  'api/stats/timeseries',
  'api/runs/:runId/feedback',
  'api/schedules',
  'api/retention',
  'api/retention/history',
  'api/retention/preview',
];

function isDefaultCollection(path: string): boolean {
  return DEFAULT_COLLECTION_ROUTES.some((pattern) => matchRoute(pattern, path) !== null);
}

function toResponse(body: unknown, status: number): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

/**
 * The route table the persistent stub below consults. Mutated by
 * `installApiMock`/its cleanup — never reassigned wholesale from outside this
 * module, so the stub's closure over it (captured once, see below) keeps
 * seeing every update.
 */
let activeOverrides: FixtureRoute[] = [];

function resolve(method: string, path: string, url: URL): Response {
  for (const route of activeOverrides) {
    if (route.method !== method) {
      continue;
    }

    const match = matchRoute(route.pattern, path);

    if (match !== null) {
      const result = route.handler(match.params, url);

      return result instanceof Response ? result : toResponse(result, route.status ?? 200);
    }
  }

  if (method === 'GET') {
    return toResponse(isDefaultCollection(path) ? [] : {}, 200);
  }

  return toResponse({}, 200);
}

/**
 * Installs the ONE persistent `fetch` stub a whole test file shares.
 *
 * `openapi-fetch` reads `globalThis.fetch` once, when `createAgentPrismClient`
 * builds the client `lib/api.ts` exports as a module-level singleton — not
 * per request. Re-stubbing `globalThis.fetch` from inside each test's
 * `beforeEach` (the more obvious design) has NO effect on `client.GET/…`
 * calls: they keep calling whatever `fetch` was global at first import, which
 * is the real one, and every screen quietly renders its error state instead
 * of the fixture. This must run once, before that first import — call it
 * from `test/setup.ts`, never from a test file itself.
 */
export function installPersistentFetchStub(): void {
  const handler = vi.fn(async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    const rawUrl = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
    const url = new URL(rawUrl, 'http://localhost');
    // `openapi-fetch` calls `fetch(request, requestInitExt)` with the method
    // already baked into the `Request` object — `requestInitExt` carries only
    // undici-specific extensions (see its `supportsRequestInitExt`), never
    // `method`. `init?.method` alone silently reads as GET for every request
    // this client makes.
    const method = (input instanceof Request ? input.method : (init?.method ?? 'GET')).toUpperCase();
    // Strips a leading UI prefix (e.g. '/agentprism/') the same way the real
    // API paths are always rooted at 'api/' or 'v1/'.
    const path = url.pathname.replace(/^\/+/, '').replace(/^.*?\b(api|v1)\//, '$1/');

    return resolve(method, path, url);
  });

  vi.stubGlobal('fetch', handler);
}

/**
 * Sets this test's route table for the persistent stub. Routes are matched
 * with the SAME pattern matcher the app's own router uses
 * (`lib/router#matchRoute`), so a fixture pattern reads exactly like the
 * `client.GET('/api/agents/{name}', …)` call it stands in for, with `{x}`
 * spelled `:x`.
 */
export function installApiMock(overrides: FixtureRoute[] = []): () => void {
  activeOverrides = overrides;

  return () => {
    activeOverrides = [];
  };
}
