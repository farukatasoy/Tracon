# @tracon/client

A typed TypeScript client for Tracon's management API, generated from its
published [OpenAPI document](https://tracon.dev/openapi/tracon.json).
Covers every `/api/*` and `/v1/*` operation: agents, runs, sessions, evals,
workflows, tenants, and the rest of the control plane. Built on
[`openapi-fetch`](https://openapi-ts.dev/openapi-fetch/) — the only runtime
dependency.

```bash
npm install @tracon/client@next
```

## Setup

```ts
import { createTraconClient } from '@tracon/client';

const client = createTraconClient({
  // The application root PLUS the MapTracon prefix. The default prefix
  // is "/tracon"; an app that called MapTracon("/control") needs
  // "https://example.com/control" instead.
  baseUrl: 'https://example.com/tracon',
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
import type { RunRecord } from '@tracon/client';
```

## Errors are thrown, not returned

`openapi-fetch` on its own returns `{ data, error }`. This package wraps it so
a failed response instead **throws** `TraconError` — a request either
resolves with `data` or the call rejects:

```ts
import { TraconError } from '@tracon/client';

try {
  await client.GET('/api/agents/{name}', { params: { path: { name: 'missing' } } });
} catch (error) {
  if (error instanceof TraconError) {
    console.error(error.status, error.title, error.detail);
  }
}
```

`title` and `detail` come from the server's `application/problem+json` body
and are shown verbatim — this package does not translate them.

## Streaming responses

Seven operations answer `text/event-stream`: agent `run`, the run event stream,
workflow `run`/`resume`/`respond`, and the OpenAI-compatible `responses` and
`chat/completions` endpoints. Request a raw stream, then use the decoder shipped
by this package:

```ts
import { readSse } from '@tracon/client';

const { response } = await client.POST('/api/agents/{name}/run', {
  params: { path: { name: 'support' } },
  body: { message: 'Where is order 4182?' },
  parseAs: 'stream',
});

for await (const frame of readSse(response)) {
  console.log(frame.event, frame.data);
}
```

`readSse` handles split network chunks and keep-alive comments. `SseDecoder` is
also exported for transports that do not expose a `fetch` `Response`. Do not use
`EventSource`: it cannot send the required `Authorization` header or issue a
`POST`.

## What this package does not cover

- **Token storage.** This package reads a token you supply — it does not read or
  write `localStorage`, a cookie, or anything else. Store it however your
  application already does.

## Why the base URL needs the prefix

`MapTracon`'s prefix is a runtime parameter, not a fixed value — the
document this package is generated from carries no prefix at all (it is
stripped as part of the generation step). If requests went to the document's
bare paths, a consumer that moved the mount point off the default
`/tracon` would get a silent `404` on every call. `baseUrl` is where the
prefix goes instead.

## Regenerating

```bash
npm run generate   # docs/openapi/tracon.json -> src/schema.ts (committed)
npm run build       # tsc -> dist/
```

`npm run generate` is a development step; it is not part of `npm run build`
and is not called by `dotnet build`. Run it after `docs/openapi/tracon.json`
changes, and commit the result — `npm test`'s `schema-drift` check fails the
build otherwise.

## Links

- Guide: <https://tracon.dev/guides/typescript-client/>
- `Tracon.Client` (the .NET counterpart, same document, same shape): <https://tracon.dev/api/>
- Capability map: <https://tracon.dev/capabilities/>

License: PolyForm Small Business 1.0.0, the same terms as its .NET counterpart
`Tracon.Client` - free below 100 people and 1,000,000 USD (2019, inflation
adjusted) revenue; a commercial licence applies above that. The text ships in this
package as LICENSE.md. Details: <https://tracon.dev/reference/licensing/>
