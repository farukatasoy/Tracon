---
title: Troubleshooting
description: Symptom-first fixes for installation, providers, tools, storage, security, streaming, background work, MCP, voice, and the console.
slug: troubleshooting
---

Start with the first failing boundary. Do not debug a model response while the agent
cannot compile, or debug the console while the API returns `401`.

<nav class="symptom-index" aria-label="Symptom index">
  <p>Find the symptom, not the subsystem. Each entry opens the section that fixes it;
  browser search still reaches every heading on this page.</p>
  <ul>
    <li><a href="#a-fast-diagnostic-order">I do not know where to start</a></li>
    <li><a href="#installation-and-startup">The package will not install, or startup throws</a></li>
    <li><a href="#agents-and-models">The agent will not resolve, or the model answers wrongly</a></li>
    <li><a href="#tools-approvals-and-guards">A tool is missing, blocked, or waiting forever</a></li>
    <li><a href="#http-access-and-api-keys">A request returns <code>401</code> or <code>403</code></a></li>
    <li><a href="#runs-streaming-and-replay">No run row, no deltas, or a cancel that does nothing</a></li>
    <li><a href="#persistence-and-migrations">Data vanishes, or a migration will not apply</a></li>
    <li><a href="#knowledge-and-memory">Knowledge returns <code>501</code>, or search stops matching</a></li>
    <li><a href="#mcp-and-skill-scripts">Remote MCP tools never appear, or a skill will not run</a></li>
    <li><a href="#voice">The voice socket returns <code>404</code>, <code>400</code>, or <code>401</code></a></li>
    <li><a href="#webhooks-retention-and-operations">A webhook is rejected, or retention previews nothing</a></li>
    <li><a href="#console">A console screen or action is not there</a></li>
    <li><a href="#native-aot">Publishing reports trimming or dynamic-code warnings</a></li>
    <li><a href="/guides/build-diagnostics/">The build reports a <code>TRC</code> diagnostic</a></li>
  </ul>
</nav>

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
app.MapTracon("/tracon", options =>
{
    options.EnableDiagnosticsEndpoint = true;
});
```

Then call `GET /tracon/api/diagnostics`. The route is absent by default, so a
`404` without this option is expected.

## Installation and startup

### NuGet says no stable version exists

Only a preview exists. Select it explicitly:

```bash
dotnet add package Tracon --prerelease
```

Pin `1.0.0-preview.6` for reproducible builds. Install the template by exact
version too:

```bash
dotnet new install Tracon.Templates@1.0.0-preview.6
```

### The generated project rejects a template option

The persistence choices are `memory`, `postgres`, `sqlite`, and `sqlserver`.
`memory` is the default. The provider choices are `openai`, `anthropic`, `google`, and
`azure`.

### A package will not target my test project

Every package you reference from application or test code targets .NET 8, 9, and
10 — `Tracon.Testing` included, so a .NET 8 LTS application can be tested with the
same helpers it runs with. Two packages are single-framework on purpose: the
`tracon` CLI is a .NET tool, and the template generates a .NET 10 application. See
[Compatibility](/reference/compatibility/).

### Restore reports `NU1107` or `NU1608` for a Tracon package

Two Tracon packages, or a Tracon package and a direct reference, ask for different
versions of the same sibling. Every Tracon package needs its siblings at exactly its own
version. Pin the whole family to one version, in `Directory.Packages.props` if you use
central package management. [Versioning](/reference/versioning/#pin-the-whole-package-family)
shows where each mix stops.

### The host stops and lists Tracon assemblies with different versions

The restore went through, but the process loaded Tracon packages from more than one
release. The check runs when the host starts and has no setting to turn it off. Read the
versions in the message, then move every Tracon package to the same one. A project that
still uses `1.0.0-preview.1` or `1.0.0-preview.2` can only be stopped here, because those
releases carry no exact range.

### Startup reports an option-validation failure

Treat it as a configuration error. Tracon validates required addresses, secrets,
positive limits, ranges, incompatible flags, and provider-specific settings during
startup or agent compilation. Read the option type named in the message in the
[API reference](/api/); do not suppress the validation.

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

Tracon never guesses prices. Add input and output prices to the model catalog or
`Tracon:Pricing`. Runs from models with no price remain counted as unknown-price
runs instead of receiving a false zero.

### Azure returns `404` for a known model

Azure calls a **deployment name**, not a model name. Put the deployment name in
`ModelBinding.Model` or `DefaultDeployment`, and verify it belongs to the configured
resource endpoint.

### Anthropic rejects the output-token request

Anthropic requires `max_tokens`. Tracon uses `DefaultMaxOutputTokens=4096` when
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
`/v1/responses`. Enable it only when the server documents that surface; Tracon
then adds `{name}-responses` as a second provider name.

## Tools, approvals, and guards

### A generated tool does not appear

Confirm all three requirements:

1. The method has `[TraconTool]`.
2. The consuming project references `Tracon.Core` so the generator runs.
3. Startup calls `AddGeneratedTools()` on the Tracon builder.

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

### An MCP server's request for input was declined

The server asked the user for something and its tool received `decline`. Read the run
events (`GET /api/runs/{id}/events`) for `tracon.tool.input.declined`; the payload's
`reason` says why:

| Reason | Meaning and fix |
|---|---|
| `no_session` | The run has no `sessionId`. Run the agent with one. A Chat Completions call and an MCP tool call never have one: use `POST /api/agents/{name}/run` |
| `no_channel` | The run has a session, but the surface cannot carry the answer: an A2A task, a Responses call with a conversation, a queued run. Use `POST /api/agents/{name}/run` |
| `child_run` | An agent called by another agent cannot ask the user. Move the tool to the agent the user talks to |
| `url_mode` | The server asked to open an address. Tracon asks for form fields only |
| `mismatch` | The server asked something other than what the user answered. A tool call stops for input once; the server must ask the same question twice or ask everything in one request |
| `no_run`, `no_termination` | The request reached the connection outside a tool call that is still running, or the tool was not called by an agent run. A server on the earlier protocol always lands here; it has no run, so no event is written and the warning is in the application log |

An answer that returns `400` or `409` never started a run. See
[A server asks the user for input](/concepts/tools/#a-server-asks-the-user-for-input)
for what each status means.

### A content guard changed an SSE stream instead of the HTTP status

After streaming headers are sent, the server cannot replace `200` with `422`. An
output block therefore arrives as an SSE error event. In a non-streaming request, the
same decision can return a normal Problem Details response with `422`.

### A pattern guard seems to do nothing

`Tracon:ContentGuard` controls pipeline behavior, but it does not invent a guard.
Register `AddPatternContentGuard()` or provide the Pattern section. The built-in guard
also starts with no denied terms and no PII patterns, so select the rules you need.

## HTTP access and API keys

### A local request works but a remote request returns `403`

Remote access is off by default. Turn it on only with a bearer token, API key, or
ASP.NET Core authorization policy:

```csharp
app.MapTracon("/tracon", options =>
{
    options.AllowRemoteAccess = true;
    options.AuthToken = builder.Configuration["Tracon:AuthToken"];
});
```

Behind a reverse proxy, configure forwarded headers. The connection address can be
the proxy's loopback address, so loopback filtering alone is not a production
boundary. A request that still carries a forwarding header counts as remote. The
[security page](/getting-started/security/) explains the `Authentication required`,
`Host not allowed`, and `Platform authority required` answers.

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
allowlist and that the main `MapTracon()` call ran before the separate external
mapping call.

### `/api/meta` does not ask for authentication

That is intentional. The console needs to learn the authentication method before it
can present a credential prompt. The endpoint returns deployment metadata but no
tenant data, agents, or secret values.

## Runs, streaming, and replay

### A run finished but there is no run row

Check three cases:

- `Tracon:RunRecording:Enabled` can be false.
- Recording is best-effort; a store failure is logged but does not stop the model
  response.
- In-memory history disappears when the process stops.

The catalog applies the recording decorator by default, but Tracon does not trade
application availability for an observability write.

### The event stream has completed messages but no text deltas

`RunRecording:RecordMessageDeltas` is false, or the provider did not stream deltas.
Completed messages remain available. Turning deltas off reduces event volume.

### A retry with `Idempotency-Key` is not an SSE stream

This is deliberate. An SSE stream cannot be reconstructed safely from a stored final
response. The idempotent form returns one JSON response and replays that response on a
matching retry. Reusing a key with a different request is rejected.

### `Prefer: respond-async` stays pending

Confirm `Tracon:AsyncRun:Enabled`, `Tracon:Scheduling:Enabled`, and
`Scheduling:RunWorker`. In a split deployment, at least one process must run the
worker. Then inspect the job state, lease owner, attempts, and error in the Jobs
screen or scheduling API.

### Replay says input is unavailable

Replay requires `RunRecording:RecordRunInput=true` at the time of the original run.
Retention can later remove run input. Neither setting can reconstruct input that was
never stored.

### Replay returns `409` for an agent that carries a client-side tool

An agent registered with [`AddClientTool`](/guides/client-side-tools/) cannot be
replayed in any tool mode (`ReplayTools`, `LiveTools`, or `NoTools`) — its body runs
in the caller's browser, not on the server, so no call to it was ever recorded and no
mode can answer one. This is not a misconfiguration; it applies even if the run being
replayed never actually called the tool, because the check looks at what the agent's
definition carries. There is no setting that lifts this limit.

### Cancellation returns not found or has no effect

Only a currently registered running execution can receive the cancellation signal.
A completed run is immutable, and a run owned by a process that died needs
reconciliation rather than an in-process signal.

## Persistence and migrations

### Everything disappears after restart

No SQL provider is active. `AddTracon()` deliberately supplies in-memory stores.
Add exactly the provider you intend to own the data path with `UsePostgreSql()`,
`UseSqlServer()`, or `UseSqlite()`. Each comes from its own package (`Tracon.PostgreSql`,
`Tracon.SqlServer`, `Tracon.Sqlite`), which the `Tracon` meta package does not include.

### SQLite reports `no such table` with `Data Source=:memory:`

Bare SQLite in-memory databases belong to one connection. Tracon opens more than
one connection, so the migration connection and store connection see different
databases. Use a shared URI:

```text
Data Source=file:tracon?mode=memory&cache=shared
```

For durable local work, use a file instead.

### PostgreSQL or SQL Server objects appear in the wrong place

Those providers use `SchemaName`, default `tracon`. SQLite has no schema and uses
`TablePrefix`, default `tracon_`. Do not configure a SQLite prefix as though it
were a schema.

### Startup reports a migration checksum mismatch

An already-applied embedded migration changed. Do not edit an applied migration,
including its comments. Restore the shipped file and add a new migration. Recreate a
schema only when losing that environment's Tracon data is acceptable.

### Migrations wait or fail to acquire a lock

Another instance can be migrating the same storage partition. PostgreSQL uses an
advisory lock, SQL Server uses `sp_getapplock`, and SQLite uses a sidecar file lock.
Check the other instance before raising a timeout. For SQLite, also check stale
process ownership and file permissions.

### `AutoApplyMigrations=false` starts with missing tables

That flag makes schema deployment your responsibility. Apply the embedded migrations
in the deployment step before application traffic starts. The optional diagnostics
report shows applied and pending state, but its endpoint must be enabled explicitly.

Requests that need a table which does not exist yet answer `503` with the title
`Database schema is not current` and the number of pending migrations, so you do
not have to read the server log to find out why a write failed. Retrying does not
help until the migrations are applied. A store that cannot be reached at all
answers `503` as well, with the title `Persistence store unavailable`; that one
usually clears on its own.

## Knowledge and memory

### Knowledge endpoints return `501`

All three dependencies must exist:

- `UsePostgreSql()` (the `Tracon.PostgreSql` package) with `EnableKnowledge = true` supplies
  `IVectorSearchStore` and pgvector storage. It is off by default; with it off, `IVectorSearchStore` never
  resolves.
- The application registers `IEmbeddingGenerator<string, Embedding<float>>`.

SQL Server and SQLite implement the other durable stores but do not implement vector
knowledge search.

### PostgreSQL migration cannot create the vector extension

`EnableKnowledge = true` is set but the server has no pgvector available, or the
migration identity lacks permission to create it. This migration set applies only
when `EnableKnowledge` is on — while it is off, no agent can request knowledge
search and this step never runs, so no extension permission is needed. Install
pgvector, or use SQL Server or SQLite when vector search is not required.

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
server uses remote HTTP transport. Tracon does not start local stdio MCP
processes. Then check connection timeout, authentication configuration-key name,
tool-count limit, and the latest refresh result.

Code tools keep priority over a remote tool with the same name. MCP tools also require
approval by default.

### MCP OAuth returns to the wrong address

Set `Tracon:Mcp:OAuthCallbackBaseUri` when proxy or public routing makes the
inferred address wrong. The callback must still use the prefix and access layers of
the mapped application.

### `MapTraconMcpServer()` says services are missing

Call `UseMcpServer()` before `builder.Build()`. Then call `MapTracon()` before
`MapTraconMcpServer()`, because the external surface reuses the main endpoint
access settings. Expose at least one agent explicitly.

### A2A startup or first request rejects an exposed agent

A2A cannot suspend a remote request for Tracon's human approval flow. Do not
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

Tracon limits the process it starts but does not isolate the operating system.
Use a container, an unprivileged user, a restricted filesystem, and restricted
network access.

## Voice

The voice symptoms (`404`, `400`, `401`, `501`, rejected audio, and a startup failure
that asks for a voice API key) are answered next to the feature, in
[Voice → Troubleshooting](/guides/voice/#troubleshooting).

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

Configuration defaults do not apply until `Tracon:Retention:Enabled=true`, and a
target with no database policy or enabled configuration default has no deletion rule.
User-data targets such as sessions, conversations, and document embeddings have no
default age even when the subsystem is enabled.

### Diagnostics or health is missing from my deployment

They use different integration points:

- diagnostics is an optional Tracon endpoint controlled by
  `EnableDiagnosticsEndpoint`;
- provider health is available through the Tracon model-health API;
- ASP.NET Core health checks need `AddTraconHealthChecks()` and your own
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

Keep the same prefix in the browser, reverse proxy, and `MapTracon()` call. The SPA
fallback can load the shell while a mismatched proxy rewrite sends its API calls to a
different path. Inspect the failing request in the browser network panel.

## Native AOT

### Publishing reports reflection or dynamic-code warnings

First identify the package or call:

- `AddTool(Delegate)` and `AddToolsFrom*()` are explicit reflection paths.
- AspNetCore, UI, MCP, Workflows, SQLite, and SQL Server do not make an AOT promise.
- `Tracon.Testing` does not make an AOT promise.

Use [Compatibility](/reference/compatibility/) to choose an AOT-safe
package set. Do not silence a warning from a public API; select a generated or
source-generated path, or accept and document that the application is not AOT-safe.

## Read next

- [Observability and cost](/guides/observability/) — the traces and metrics that answer a question before it becomes a symptom
- [Reliable runs](/guides/reliability/) — the failure boundaries that stop several of these symptoms recurring
- [Configuration](/reference/configuration/) — the option behind most of the fixes above
