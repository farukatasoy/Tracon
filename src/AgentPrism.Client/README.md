# AgentPrism.Client

A typed client for AgentPrism's management API, generated from its published
[OpenAPI document](https://agentprism.doayen.web.tr/openapi/agentprism.json).
Covers every `/api/*` operation: agents, runs, sessions, evals, workflows,
tenants, and the rest of the control plane.

```bash
dotnet add package AgentPrism.Client
```

## Setup

```csharp
services.AddAgentPrismClient(options =>
{
    // The application root PLUS the MapAgentPrism prefix. The default
    // prefix is "/agentprism"; an app that called MapAgentPrism("/control")
    // needs "https://example.com/control/" instead.
    options.BaseAddress = new Uri("https://example.com/agentprism/");
    options.Token = configuration["AgentPrism:Token"]; // a secret - read it, don't hard-code it
});
```

Resolve `AgentPrismApiClient` from the container and call any of its 160
generated methods:

```csharp
var client = provider.GetRequiredService<AgentPrismApiClient>();
var agents = await client.AgentPrismListAgentsAsync(cancellationToken);
```

## Why the base address needs the prefix

`MapAgentPrism`'s prefix is a runtime parameter, not a fixed value — the
document every operation is generated from carries **no** prefix at all. If
the client sent requests to the document's bare paths, a consumer that moved
the mount point off the default `/agentprism` would get a silent `404` on
every call. `AgentPrismClientOptions.BaseAddress` is where the prefix goes
instead: it must be the address `MapAgentPrism` is actually mounted at, with a
trailing `/`.

## The DTOs are not `AgentPrism.Abstractions` types

`AgentPrismApiClient`'s request and response types are generated straight
from the OpenAPI document. They are not the same CLR types as
`AgentPrism.Abstractions`'s `RunRecord`, `AgentDefinition`, and so on — same
shape, different type, because generating against the abstractions package
would fight the code generator and would leak server-side interfaces
(`IRunStore` and similar) into an HTTP consumer that never needs them.

## Dependencies

No AgentPrism package. One NuGet package —
`Microsoft.Extensions.DependencyInjection.Abstractions`, for
`AddAgentPrismClient(this IServiceCollection ...)` — which carries no
transitive dependencies of its own. Serialization goes through a
source-generated `JsonSerializerContext`, not runtime reflection;
`IHttpClientFactory` is deliberately not used, for the same "no extra
package" reason `AgentPrism.Voice` avoids it.

## Links

- CLI (`agentprism` global tool, uses this package for `health`): <https://agentprism.doayen.web.tr/guides/cli/>
- Capability map: <https://agentprism.doayen.web.tr/capabilities/>
- API reference: <https://agentprism.doayen.web.tr/api/>
