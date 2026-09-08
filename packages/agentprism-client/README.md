# @agentprism/client

A typed TypeScript client for AgentPrism's management API, generated from its
published [OpenAPI document](https://agentprism.doayen.web.tr/openapi/agentprism.json).
Covers every `/api/*` and `/v1/*` operation: agents, runs, sessions, evals,
workflows, tenants, and the rest of the control plane. Built on
[`openapi-fetch`](https://openapi-ts.dev/openapi-fetch/) — the only runtime
dependency.

```bash
npm install @agentprism/client
```

## Setup

```ts
import { createAgentPrismClient } from '@agentprism/client';

const client = createAgentPrismClient({
  // The application root PLUS the MapAgentPrism prefix. The default prefix
  // is "/agentprism"; an app that called MapAgentPrism("/control") needs
  // "https://example.com/control" instead.
  baseUrl: 'https://example.com/agentprism',
  token: 'a-secret-token', // or a function called per request
  onUnauthorized: () => {
    /* the server answered 401 — your app decides what that means */
  },
});

const { data: agents } = await client.GET('/api/agents');
```

Every call is typed against the OpenAPI document: the path, its parameters,
its request body, and its response are all checked at compile time. A schema
type is also exported by name for every schema in the document, so a
response can be typed without indexing into `components`:

```ts
import type { RunRecord } from '@agentprism/client';
```

## Errors are thrown, not returned

`openapi-fetch` on its own returns `{ data, error }`. This package wraps it so
a failed response instead **throws** `AgentPrismError` — a request either
resolves with `data` or the call rejects:

```ts
import { AgentPrismError } from '@agentprism/client';

try {
  await client.GET('/api/agents/{name}', { params: { path: { name: 'missing' } } });
} catch (error) {
  if (error instanceof AgentPrismError) {
    console.error(error.status, error.title, error.detail);
  }
}
```

`title` and `detail` come from the server's `application/problem+json` body
and are shown verbatim — this package does not translate them.

## What this package does not cover

- **Server-Sent Events.** Six operations (agent `run`, workflow
  `run`/`resume`/`respond`, and the OpenAI-compatible `responses` and
  `chat/completions` endpoints) answer `text/event-stream`, not JSON. A
  `fetch`-based typed client reads a response body as JSON; call these with
  your own `EventSource`/`fetch` streaming code instead, using this
  package's `paths` types for the request shape.
- **Token storage.** This package reads a token you supply — it does not
  read or write `localStorage`, a cookie, or anything else. Store it however
  your application already does.

## Why the base URL needs the prefix

`MapAgentPrism`'s prefix is a runtime parameter, not a fixed value — the
document this package is generated from carries no prefix at all (it is
stripped as part of the generation step). If requests went to the document's
bare paths, a consumer that moved the mount point off the default
`/agentprism` would get a silent `404` on every call. `baseUrl` is where the
prefix goes instead.

## Regenerating

```bash
npm run generate   # docs/openapi/agentprism.json -> src/schema.ts (committed)
npm run build       # tsc -> dist/
```

`npm run generate` is a development step; it is not part of `npm run build`
and is not called by `dotnet build`. Run it after `docs/openapi/agentprism.json`
changes, and commit the result — `npm test`'s `schema-drift` check fails the
build otherwise.

## Links

- Guide: <https://agentprism.doayen.web.tr/guides/typescript-client/>
- `AgentPrism.Client` (the .NET counterpart, same document, same shape): <https://agentprism.doayen.web.tr/api/>
- Capability map: <https://agentprism.doayen.web.tr/capabilities/>

License: PolyForm Small Business 1.0.0, the same terms as its .NET counterpart
`AgentPrism.Client` - free below 100 people and 1,000,000 USD (2019, inflation
adjusted) revenue; a commercial licence applies above that. The text ships in this
package as LICENSE.md. Details: <https://agentprism.doayen.web.tr/reference/licensing/>
