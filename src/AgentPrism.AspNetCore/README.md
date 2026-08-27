# AgentPrism.AspNetCore

The HTTP layer. One call maps the whole control plane into an existing ASP.NET Core
application.

```bash
dotnet add package AgentPrism.AspNetCore
```

```csharp
builder.AddAgentPrism()
       .UseOpenAI(apiKey)
       .UsePostgreSql(connectionString);

var app = builder.Build();

app.MapAgentPrism("/agentprism");
```

143 operations over 112 paths, all under the prefix you choose. The prefix appears in
exactly one place; nothing else in your application has to know it.

## Two endpoint groups

**The management API** — everything the control plane needs: agents and their version
history, runs and their event streams, sessions, skills, workflows, evals,
experiments, the job queue, quotas, retention, webhooks, API keys, the audit trail,
and diagnostics.

**OpenAI-compatible endpoints** — `/v1/responses`, `/v1/conversations`, and
`/v1/chat/completions`, so an existing OpenAI client can point at your agents without
being rewritten.

Streaming endpoints answer with `text/event-stream`. A run can also be queued instead
of streamed by sending `Prefer: respond-async`, which returns `202 Accepted` with a
`Location` header, and deduplicated by sending `Idempotency-Key`.

## Access is layered

Each layer is independent; use as many as you need.

| Layer | Default | What it does |
|---|---|---|
| Loopback restriction | **on** | Nothing but `localhost` reaches the endpoints until you turn it off |
| Bearer token | off | `AuthToken` is compared in constant time |
| API keys | off | Keys issued from `/api/keys`, stored hashed, scoped per capability |
| Authorization policy | off | Your own ASP.NET Core policy runs on every endpoint |
| Role policies | off | Reader / Operator / Admin separated per endpoint |

```csharp
app.MapAgentPrism("/agentprism", options =>
{
    options.AllowRemoteAccess = true;
    options.RequireAuthorization("AgentPrismAdmin");
    options.RequireRolePolicies = true;
});
```

The safe default is deliberate: an application that maps AgentPrism and forgets to
configure anything is not exposed to the network. Never ship `AllowRemoteAccess` on
without a token, a key, or a policy behind it.

> The UI shell is exempt from the bearer layer — a browser cannot attach an
> `Authorization` header to a script or stylesheet request. The loopback restriction
> and the authorization policy still apply to it.

## OpenAPI: metadata only, no dependency

Every route carries `WithName`, `WithTags`, `WithSummary`, `WithDescription`,
`Accepts`, and `Produces`. The package itself does **not** reference
`Microsoft.AspNetCore.OpenApi`, so it cannot force that dependency — and the CVE-bearing
`Microsoft.OpenApi` version it drags in — onto your graph.

Call `AddOpenApi()` in your own application and the AgentPrism endpoints appear in
your document, fully described, with your title and your servers.

A snapshot of the generated document is published on the documentation site.

## Pre-release dependencies live here

This package depends on `Microsoft.Agents.AI.Hosting` (preview) and
`Microsoft.Agents.AI.Hosting.OpenAI` (alpha). Every pre-release dependency in
AgentPrism is deliberately concentrated in this one package, so a consumer that uses
only the runtime never takes one.

## Also in this package

`MapAgentPrismMcpServer()` exposes your agents as MCP tools, and `MapAgentPrismA2A()`
speaks the agent-to-agent protocol. Both must be mapped **after** `MapAgentPrism` so
they inherit its access settings. Set `AgentPrismMcpServerOptions.EnableTasks` to
serve a long-running MCP call as a pollable task instead of holding the connection
open; it defaults to `false`.

## Compatibility

Targets `net8.0`, `net9.0`, and `net10.0`. This package is **not** AOT-compatible —
the hosting dependencies are not — while the runtime packages below it are.

## Links

- Full documentation: <https://agentprism.doayen.web.tr>
- HTTP API reference: <https://agentprism.doayen.web.tr/http-api/>

License: MIT
