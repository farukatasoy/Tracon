# Tracon.Client

A typed client for Tracon's management API, generated from its published
[OpenAPI document](https://tracon.dev/openapi/tracon.json).
Covers every `/api/*` operation: agents, runs, sessions, evals, workflows,
tenants, and the rest of the control plane.

```bash
dotnet add package Tracon.Client
```

## Setup

```csharp
services.AddTraconClient(options =>
{
    // The application root PLUS the MapTracon prefix. The default
    // prefix is "/tracon"; an app that called MapTracon("/control")
    // needs "https://example.com/control/" instead.
    options.BaseAddress = new Uri("https://example.com/tracon/");
    options.Token = configuration["Tracon:Token"]; // a secret - read it, don't hard-code it
});
```

Resolve `TraconApiClient` from the container and call any of its generated
methods — one per operation in the document:

```csharp
var client = provider.GetRequiredService<TraconApiClient>();
var agents = await client.TraconListAgentsAsync(cancellationToken);
```

## Streaming endpoints have a second method

Some endpoints answer with Server-Sent Events rather than JSON. Each of them has
a `...StreamAsync` sibling that hands you one raw SSE frame at a time, as the
server flushes it:

```csharp
await foreach (var frame in client.TraconRunAgentStreamAsync("support", request))
{
    Console.WriteLine(frame); // "id: 3\nevent: update\ndata: {...}"
}
```

Leaving the loop early, or cancelling the token, stops the read and releases the
connection. Comment-only keep-alive frames are skipped.

The two OpenAI-compatible endpoints (`/v1/responses`, `/v1/chat/completions`)
answer with **either** shape, chosen by the `stream` flag in the request body, so
they have both methods and you pick one:

```csharp
var body = JsonSerializer.SerializeToElement(
    new { model = "support", input = "Where is order 4182?", stream = true });

await foreach (var frame in client.TraconOpenAIResponsesStreamAsync(body))
{
    Console.WriteLine(frame);
}
```

The body is sent exactly as written — the client never rewrites `stream` to match
the method. If the two disagree, the call throws `TraconApiException` with a
message naming the fix, instead of failing obscurely later.

## Why the base address needs the prefix

`MapTracon`'s prefix is a runtime parameter, not a fixed value — the
document every operation is generated from carries **no** prefix at all. If
the client sent requests to the document's bare paths, a consumer that moved
the mount point off the default `/tracon` would get a silent `404` on
every call. `TraconClientOptions.BaseAddress` is where the prefix goes
instead: it must be the address `MapTracon` is actually mounted at, with a
trailing `/`.

## The DTOs are not `Tracon.Abstractions` types

`TraconApiClient`'s request and response types are generated straight
from the OpenAPI document. They are not the same CLR types as
`Tracon.Abstractions`'s `RunRecord`, `AgentDefinition`, and so on — same
shape, different type, because generating against the abstractions package
would fight the code generator and would leak server-side interfaces
(`IRunStore` and similar) into an HTTP consumer that never needs them.

## Dependencies

No Tracon package. One NuGet package —
`Microsoft.Extensions.DependencyInjection.Abstractions`, for
`AddTraconClient(this IServiceCollection ...)` — which carries no
transitive dependencies of its own. Serialization goes through a
source-generated `JsonSerializerContext`, not runtime reflection;
`IHttpClientFactory` is deliberately not used, for the same "no extra
package" reason `Tracon.Voice` avoids it.

## Links

- CLI (`tracon` global tool, uses this package for `health`): <https://tracon.dev/guides/cli/>
- Capability map: <https://tracon.dev/capabilities/>
- API reference: <https://tracon.dev/api/>

Licence: PolyForm Small Business 1.0.0 - free below 100 people and 1,000,000 USD
(2019, inflation adjusted) revenue; a commercial licence applies above that. Terms
ship in the package as LICENSE.md. Details: <https://tracon.dev/reference/licensing/>
