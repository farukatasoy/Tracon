---
title: Troubleshooting
description: Symptom-first fixes for installation, providers, tools, storage, security, streaming, background work, MCP, voice, and the console.
slug: troubleshooting
---

Start with the first failing boundary. Do not debug a model response while the agent
cannot compile, or debug the console while the API returns `401`.

## A fast diagnostic order

1. Confirm package version and target framework.
2. Confirm every required `Use*()` registration ran before `builder.Build()`.
3. Confirm the agent resolves from `IAgentCatalog`.
4. Call the HTTP endpoint without the console.
5. Check authentication, role, tenant, and API-key scope.
6. Check storage and background-worker health.
7. Check the provider last.

The optional diagnostics report is useful after the access boundary is correct:

```csharp
app.MapAgentPrism("/agentprism", options =>
{
    options.EnableDiagnosticsEndpoint = true;
});
```

Then call `GET /agentprism/api/diagnostics`. The route is absent by default, so a
`404` without this option is expected.

## Installation and startup

### NuGet says no stable version exists

AgentPrism is a preview package. Select pre-release versions explicitly:

```bash
dotnet add package AgentPrism --prerelease
```

For reproducible builds, pin the exact preview version instead. Install the template
with the same rule, for example
`dotnet new install AgentPrism.Templates@1.0.0-preview.N` after replacing `N` with a
published version.

### The generated project rejects a template option

The persistence choices are `memory`, `postgres`, `sqlite`, and `sqlserver`.
`memory` is the default. The provider choices are `openai`, `anthropic`, `google`, and
`azure`.

### A package will not target my test project

Runtime packages target .NET 8, 9, and 10. `AgentPrism.Testing` targets .NET 10 only,
and the template generates a .NET 10 application. See
[Compatibility](/AgentPrism/reference/compatibility/).

### Startup reports an option-validation failure

Treat it as a configuration error. AgentPrism validates required addresses, secrets,
positive limits, ranges, incompatible flags, and provider-specific settings during
startup or agent compilation. Read the option type named in the message in the
[API reference](/AgentPrism/api/); do not suppress the validation.

Common causes include:

- a cloud-provider registration with no API key and no supported alternative credential;
- Azure registration with no resource endpoint;
- script execution enabled without the isolation acknowledgement;
- a non-positive timeout, lease, size, or concurrency limit;
- an unknown provider-specific key in `ModelBinding.ProviderSettings`;
- bare SQLite `Data Source=:memory:`.

## Agents and models

### The agent is listed but will not resolve

The definition compiler refuses unresolved or unsafe references. Check the reported
items in this order:

- provider name and model binding;
- response-format combination;
- every tool and skill name;
- every callable-agent name;
- cycles and graph limits;
- MCP resource support and URI limits;
- memory features that need a missing service.

`POST /api/agents/validate` runs these checks without saving the definition and
without calling a model.

### A model is absent from the console picker

Provider catalogs start empty. They are not validation lists. Add a `ModelDescriptor`
to provider configuration when you want selection metadata, prices, and a health row.
An agent can still use an unlisted model name.

### The model works but cost is blank

AgentPrism never guesses prices. Add input and output prices to the model catalog or
`AgentPrism:Pricing`. Runs from models with no price remain counted as unknown-price
runs instead of receiving a false zero.

### Azure returns `404` for a known model

Azure calls a **deployment name**, not a model name. Put the deployment name in
`ModelBinding.Model` or `DefaultDeployment`, and verify it belongs to the configured
resource endpoint.

### Anthropic rejects the output-token request

Anthropic requires `max_tokens`. AgentPrism uses `DefaultMaxOutputTokens=4096` when
the model binding has no limit. Set a value suitable for the selected model and your
provider account. A very high bound can also fail an upstream budget check before
generation starts.

### Google returns an empty answer without a transport error

A Gemini safety decision can produce an empty candidate. Inspect the run's provider
metadata and the configured `google.safety.*` settings before treating it as a
network failure.

### An OpenAI-compatible server works for chat but not Responses

`UseOpenAICompatible()` registers the Chat Completions surface by default.
`EnableResponsesSurface` is off because many compatible servers do not implement
`/v1/responses`. Enable it only when the server documents that surface; AgentPrism
then adds `{name}-responses` as a second provider name.

## Tools, approvals, and guards

### A generated tool does not appear

Confirm all three requirements:

1. The method has `[AgentPrismTool]`.
2. The consuming project references `AgentPrism.Core` so the generator runs.
3. Startup calls `AddGeneratedTools()` on the AgentPrism builder.

A helper method without the attribute is intentionally ignored.

### A tool fails when it resolves a dependency

Do not expect a model-invoked tool to receive your application's service provider at
call time. Resolve or create the dependency while you register the tool, then capture
it in the delegate or tool object. This also makes the lifetime visible and testable.

### `AddTool()` or `AddToolsFrom()` produces trimming warnings

The delegate and scan convenience paths use reflection. The warnings are deliberate.
For trimming or native AOT, use `AddGeneratedTools()` or register an already-built
`AIFunction`. Do not suppress the warnings and assume the metadata will survive.

### A run waits forever for approval

First distinguish the execution mode:

- A streaming run carries the approval through its interaction.
- A queued run closes as `AwaitingApproval`; a decision enqueues continuation work.

For queued work, confirm the scheduling worker runs, the approval has not expired,
and the tenant in the decision matches the request. A standing rule can prevent a
future prompt, but it does not retroactively decide an existing request.

### A content guard changed an SSE stream instead of the HTTP status

After streaming headers are sent, the server cannot replace `200` with `422`. An
output block therefore arrives as an SSE error event. In a non-streaming request, the
same decision can return a normal Problem Details response with `422`.

### A pattern guard seems to do nothing

`AgentPrism:ContentGuard` controls pipeline behavior, but it does not invent a guard.
Register `AddPatternContentGuard()` or provide the Pattern section. The built-in guard
also starts with no denied terms and no PII patterns, so select the rules you need.

## HTTP access and API keys

### A local request works but a remote request returns `403`

Remote access is off by default. Turn it on only with a bearer token, API key, or
ASP.NET Core authorization policy:

```csharp
app.MapAgentPrism("/agentprism", options =>
{
    options.AllowRemoteAccess = true;
    options.AuthToken = builder.Configuration["AgentPrism:AuthToken"];
});
```

Behind a reverse proxy, configure forwarded headers. The connection address can be
the proxy's loopback address, so loopback filtering alone is not a production
boundary.

### The shell opens but every data request returns `401`

This is an intentional split. Browser scripts cannot attach an authorization header
while the shell assets load, so HTML, JavaScript, and CSS can load before the console
asks for a token. Every data request remains protected. Enter a valid token or API
key; the console keeps it in `sessionStorage` and removes it when the tab closes.

### A valid API key returns `403`

Check both authorization dimensions:

1. The caller must satisfy the endpoint's Reader, Operator, or Admin role policy when
   those policies are active.
2. The key must carry the required closed `ApiKeyScope` value.

Scopes narrow roles. They never grant a missing role. JSON scope names use PascalCase,
such as `RunsWrite`, not `runs:write`. Keys are managed under `/api/api-keys`.

### MCP server or A2A invocation returns `403`

External protocol surfaces require `ExternalInvoke`. An internal automation key with
`RunsWrite` does not open them. Also confirm the agent is on the explicit exposure
allowlist and that the main `MapAgentPrism()` call ran before the separate external
mapping call.

### `/api/meta` does not ask for authentication

That is intentional. The console needs to learn the authentication method before it
can present a credential prompt. The endpoint returns deployment metadata but no
tenant data, agents, or secret values.

## Runs, streaming, and replay

### A run finished but there is no run row

Check three cases:

- `AgentPrism:RunRecording:Enabled` can be false.
- Recording is best-effort; a store failure is logged but does not stop the model
  response.
- In-memory history disappears when the process stops.

The catalog applies the recording decorator by default, but AgentPrism does not trade
application availability for an observability write.

### The event stream has completed messages but no text deltas

`RunRecording:RecordMessageDeltas` is false, or the provider did not stream deltas.
Completed messages remain available. Turning deltas off reduces event volume.

### A retry with `Idempotency-Key` is not an SSE stream

This is deliberate. An SSE stream cannot be reconstructed safely from a stored final
response. The idempotent form returns one JSON response and replays that response on a
matching retry. Reusing a key with a different request is rejected.

### `Prefer: respond-async` stays pending

Confirm `AgentPrism:AsyncRun:Enabled`, `AgentPrism:Scheduling:Enabled`, and
`Scheduling:RunWorker`. In a split deployment, at least one process must run the
worker. Then inspect the job state, lease owner, attempts, and error in the Jobs
screen or scheduling API.

### Replay says input is unavailable

Replay requires `RunRecording:RecordRunInput=true` at the time of the original run.
Retention can later remove run input. Neither setting can reconstruct input that was
never stored.

### Cancellation returns not found or has no effect

Only a currently registered running execution can receive the cancellation signal.
A completed run is immutable, and a run owned by a process that died needs
reconciliation rather than an in-process signal.

## Persistence and migrations

### Everything disappears after restart

No SQL provider is active. `AddAgentPrism()` deliberately supplies in-memory stores.
Add exactly the provider you intend to own the data path with `UsePostgreSql()`,
`UseSqlServer()`, or `UseSqlite()`.

### SQLite reports `no such table` with `Data Source=:memory:`

Bare SQLite in-memory databases belong to one connection. AgentPrism opens more than
one connection, so the migration connection and store connection see different
databases. Use a shared URI:

```text
Data Source=file:agentprism?mode=memory&cache=shared
```

For durable local work, use a file instead.

### PostgreSQL or SQL Server objects appear in the wrong place

Those providers use `SchemaName`, default `agentprism`. SQLite has no schema and uses
`TablePrefix`, default `agentprism_`. Do not configure a SQLite prefix as though it
were a schema.

### Startup reports a migration checksum mismatch

An already-applied embedded migration changed. Do not edit an applied migration,
including its comments. Restore the shipped file and add a new migration. Recreate a
schema only when losing that environment's AgentPrism data is acceptable.

### Migrations wait or fail to acquire a lock

Another instance can be migrating the same storage partition. PostgreSQL uses an
advisory lock, SQL Server uses `sp_getapplock`, and SQLite uses a sidecar file lock.
Check the other instance before raising a timeout. For SQLite, also check stale
process ownership and file permissions.

### `AutoApplyMigrations=false` starts with missing tables

That flag makes schema deployment your responsibility. Apply the embedded migrations
in the deployment step before application traffic starts. The optional diagnostics
report shows applied and pending state, but its endpoint must be enabled explicitly.

## Knowledge and memory

### Knowledge endpoints return `501`

Both dependencies must exist:

- `UsePostgreSql()` supplies `IVectorSearchStore` and pgvector storage.
- The application registers `IEmbeddingGenerator<string, Embedding<float>>`.

SQL Server and SQLite implement the other durable stores but do not implement vector
knowledge search.

### PostgreSQL migration cannot create the vector extension

The server must have pgvector available, and the migration identity needs permission
to create or use the extension. The PostgreSQL migration creates that extension even
when no agent uses knowledge search. Use SQL Server or SQLite when pgvector cannot be
installed and vector search is not required.

### Search fails after changing the embedding model

Check vector dimensions. `Knowledge:Dimensions` defaults to 1536 and becomes part of
the PostgreSQL vector column type. A model with another dimension needs a schema
migration and re-embedding of existing content.

### Session branching is unavailable in memory

Branching needs addressable stored conversation items. Use a SQL provider. The
in-memory session path is valid for conversation execution but does not promise a
durable item graph.

## MCP and skill scripts

### Remote MCP tools never appear

Confirm `UseMcp()` ran, the server record is enabled for the current tenant, and the
server uses remote HTTP transport. AgentPrism does not start local stdio MCP
processes. Then check connection timeout, authentication configuration-key name,
tool-count limit, and the latest refresh result.

Code tools keep priority over a remote tool with the same name. MCP tools also require
approval by default.

### MCP OAuth returns to the wrong address

Set `AgentPrism:Mcp:OAuthCallbackBaseUri` when proxy or public routing makes the
inferred address wrong. The callback must still use the prefix and access layers of
the mapped application.

### `MapAgentPrismMcpServer()` says services are missing

Call `UseMcpServer()` before `builder.Build()`. Then call `MapAgentPrism()` before
`MapAgentPrismMcpServer()`, because the external surface reuses the main endpoint
access settings. Expose at least one agent explicitly.

### A2A startup or first request rejects an exposed agent

A2A cannot suspend a remote request for AgentPrism's human approval flow. Do not
expose an agent whose reachable tools require approval. Use an internal run surface
for that agent or expose a purpose-built agent with an approval-free tool set.

### A skill script is present but will not run

All gates must pass:

- script execution is enabled;
- platform isolation is acknowledged;
- the file extension maps to an allowed interpreter path;
- the tenant has a valid grant;
- stored scripts are separately allowed when the script came from the database;
- concurrency, argument, output, and timeout limits permit the call.

AgentPrism limits the process it starts but does not isolate the operating system.
Use a container, an unprivileged user, a restricted filesystem, and restricted
network access.

## Voice

### The voice WebSocket route returns `404`

Call `UseVoiceConversation()` before `builder.Build()`. The route is conditional and
does not exist without that registration. `MapAgentPrism()` maps it; there is no
separate voice mapping call.

### The WebSocket request returns `400`

The request did not complete a WebSocket upgrade. Use a WebSocket client and the
mapped route `/api/voice/sessions/{sessionId}/stream`. `MapAgentPrism()` installs the
required middleware when voice conversation is registered.

### The WebSocket request returns `401`

Browsers cannot add an `Authorization` header to the handshake. Send the token in the
documented `Sec-WebSocket-Protocol` subprotocol. Do not put it in the query string;
URLs reach browser, server, and proxy logs.

### Voice conversation reports `501`

The conversation driver exists, but transcription or synthesis does not. Register
both `ISpeechTranscriber` and `ISpeechSynthesizer`. `AgentPrism.Voice.UseVoice()`
provides the built-in ElevenLabs pair when its key and model settings are valid.

### Synthesized audio is rejected as an attachment

The attachment store validates file signatures. The built-in default
`mp3_44100_128` has a detectable MP3 format. Headerless PCM and µ-law output formats
cannot pass the default attachment guard without a deliberate custom storage and
validation design.

## Webhooks, retention, and operations

### A webhook URL is rejected before delivery

HTTPS is required by default. Private, loopback, link-local, and cloud-metadata
targets are blocked, redirects are not followed, and DNS is checked at socket
connection time. Change `AllowPrivateNetworkTargets` or `AllowInsecureHttp` only for
a controlled network with a documented reason. Insecure HTTP remains limited to
loopback even when `AllowInsecureHttp` is true.

### A webhook stopped after repeated failures

Subscriptions disable after 20 consecutive failures by default. Inspect delivery
attempts and the final response or transport error, fix the receiver, then re-enable
the subscription.

### Retention preview shows nothing

Configuration defaults do not apply until `AgentPrism:Retention:Enabled=true`, and a
target with no database policy or enabled configuration default has no deletion rule.
User-data targets such as sessions, conversations, and document embeddings have no
default age even when the subsystem is enabled.

### Diagnostics or health is missing from my deployment

They use different integration points:

- diagnostics is an optional AgentPrism endpoint controlled by
  `EnableDiagnosticsEndpoint`;
- provider health is available through the AgentPrism model-health API;
- ASP.NET Core health checks need `AddAgentPrismHealthChecks()` and your own
  `MapHealthChecks()` route.

## Console

### A console action is absent

The UI hides actions the effective role cannot perform. It also keeps code-owned
objects read-only. Confirm the current role summary from `/api/meta`, the object's
origin, and the API-key scopes before treating a missing button as a rendering bug.

### I cannot find tenant management

The console shows the current tenant but has no tenant-management screen. Use the
management API and configure request resolution with `UseTenancy()`. This keeps the
tenant trust boundary in host code.

### I cannot create a tool in the console

That is a security boundary. Define executable tools in code, register them, and then
assign their names to database agents in the console. A stored skill script is a
different feature with separate isolation and grant gates.

### A deep link returns the app but not the expected data

Keep the same prefix in the browser, reverse proxy, and `MapAgentPrism()` call. The SPA
fallback can load the shell while a mismatched proxy rewrite sends its API calls to a
different path. Inspect the failing request in the browser network panel.

## Native AOT

### Publishing reports reflection or dynamic-code warnings

First identify the package or call:

- `AddTool(Delegate)` and `AddToolsFrom*()` are explicit reflection paths.
- AspNetCore, UI, MCP, Workflows, SQLite, and SQL Server do not make an AOT promise.
- `AgentPrism.Testing` does not make an AOT promise.

Use [Compatibility](/AgentPrism/reference/compatibility/) to choose an AOT-safe
package set. Do not silence a warning from a public API; select a generated or
source-generated path, or accept and document that the application is not AOT-safe.
