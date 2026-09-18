---
title: Troubleshooting
description: Symptom-first fixes for installation, providers, tools, storage, security, streaming, background work, MCP, voice, and the console.
slug: troubleshooting
---

:::caution[Package availability]
Tracon packages and templates are not published yet. Package-install examples on
this page describe the release form and do not currently resolve from public
registries. With authorized repository access, use the
[source build instructions](/getting-started/first-agent/).
:::

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
    <li><a href="#build-diagnostics-and-the-agent-map">The build reports an <code>APG</code> diagnostic</a></li>
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

Tracon has not been published yet: no version exists on NuGet at all, stable
or pre-release. Build from a clone of the repository until the first release.

Once it is published, it will be a preview package, so select pre-release
versions explicitly:

```bash
dotnet add package Tracon --prerelease
```

For reproducible builds, pin the exact preview version instead. Install the template
with the same rule, for example
`dotnet new install Tracon.Templates@1.0.0-preview.N` after replacing `N` with a
published version.

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
`UseSqlServer()`, or `UseSqlite()`.

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

- `UsePostgreSql()` with `EnableKnowledge = true` supplies `IVectorSearchStore` and
  pgvector storage. It is off by default; with it off, `IVectorSearchStore` never
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

### The voice WebSocket route returns `404`

Call `UseVoiceConversation()` before `builder.Build()`. The route is conditional and
does not exist without that registration. `MapTracon()` maps it; there is no
separate voice mapping call.

### The WebSocket request returns `400`

The request did not complete a WebSocket upgrade. Use a WebSocket client and the
mapped route `/api/voice/sessions/{sessionId}/stream`. `MapTracon()` installs the
required middleware when voice conversation is registered.

### The WebSocket request returns `401`

Browsers cannot add an `Authorization` header to the handshake. Send the token in the
documented `Sec-WebSocket-Protocol` subprotocol. Do not put it in the query string;
URLs reach browser, server, and proxy logs.

### Voice conversation reports `501`

The conversation driver exists, but transcription or synthesis does not. Register
both `ISpeechTranscriber` and `ISpeechSynthesizer`. `Tracon.Voice.UseVoice()`
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

## Build diagnostics and the agent map

### The build reports an APG diagnostic

`Tracon.Core` ships an analyzer with two families.

| Ids | Category | What it reports |
|---|---|---|
| `TRC0001`–`TRC0012` | `Tracon.Tools` | A method marked `[TraconTool]` cannot be generated, a parameter has no description, or a constraint attribute does not apply. Errors: fix the method. `TRC0009` and `TRC0010` are warnings. |
| `TRC0101`, `TRC0102` | `Tracon.Usage` | A registration this compilation never makes. The application fails at run time. |
| `TRC0201` | `Tracon.Usage` | A definition carries a literal secret instead of the name of a configuration key. |
| `TRC0301`, `TRC0302` | `Tracon.Usage` | Code written by hand for behaviour the package already ships. |
| `TRC0401` | `Tracon.Usage` | `AGENTS.md` was generated from an older capability map. |
| `TRC0402` | `Tracon.Usage` | The local reference file is written, and your own `AGENTS.md` never names it, so the map is unreachable. |
| `TRC0403` | `Tracon.Usage` | The gate skill `tracon agent-skill` wrote was stamped with an older capability map. |
| `TRC0501` | `Tracon.Usage` | An async iterator writes ambient state once but a loop advances the enumeration without repeating it. |
| `TRC0502` | `Tracon.Usage` | An ambient scope's `Begin(...)` result is discarded, so it is never restored. |

Each message names the API that resolves it, and each diagnostic links to the
section of the [capability map](/capabilities/) that documents it.

### A tool name is invalid (TRC0002)

A method marked `[TraconTool]` has a name — the attribute argument, or the method
name when none is given — outside the range a tool name may use. A tool name must be
1-64 characters and contain only letters, digits, `_`, or `-`.

The name reaches the model as-is, and a model's own tool-calling protocol rejects
names outside this range before the call ever reaches Tracon. Rename the method,
or give an explicit name to `[TraconTool("valid-name")]`.

### A tool parameter type is unsupported (TRC0003)

The generator produces a JSON Schema for each parameter from its .NET type, and it
recognizes primitive types, `string`, `Guid`, `DateTime`/`DateTimeOffset`, `enum`,
arrays or `IReadOnlyList<T>` of these, `CancellationToken`, and a supported **object**
(a public record or class with a single public constructor — see [Write your own
tool](/guides/write-your-own-tool/)), up to 3 nested object levels deep. A parameter
of any other type — a `Dictionary<,>`, a tuple, a type with more than one public
constructor, an object graph deeper than 3 levels or containing a cycle (`TRC0012`) —
has no schema mapping and is reported instead of silently ignored.

**The generator's expression boundary, stated once:** it can express a parameter's
scalar type, array shape, object shape (and arrays of objects), `description`,
whether it is required, and a `minimum`, `maximum`, length, or `pattern` constraint
from a standard `System.ComponentModel.DataAnnotations` attribute (`RangeAttribute`,
`MinLengthAttribute`, `MaxLengthAttribute`, `StringLengthAttribute`,
`RegularExpressionAttribute`). An object parameter's own type, and every nested
object type in its graph, must be declared with `[JsonSerializable]` on the
`JsonSerializerContext` the tool points at (`TRC0011`) — the generator never emits
its own context. There is no partial path beyond this boundary: a parameter
either gets an exact schema within it, or it needs the escape route below.

Change the parameter to a supported type, or register the tool by hand instead of
through `[TraconTool]`:

```csharp
builder.AddTracon()
       .AddTool(AIFunctionFactory.Create(MyMethod));
```

`AIFunctionFactory.Create` builds the schema itself and accepts a wider range of
parameter shapes, including a nested object — reflection maps its properties at run
time instead of at compile time. For an AOT-safe version of the same call, give it a
source-generated `JsonSerializerOptions` instead of relying on reflection:

```csharp
public sealed record OrderFilter(string Status, int MinAmount);

[JsonSerializable(typeof(OrderFilter))]
internal partial class OrderFilterJsonContext : JsonSerializerContext;

builder.AddTracon()
       .AddTool(AIFunctionFactory.Create(
           (OrderFilter filter) => SearchOrders(filter),
           "search_orders",
           "Searches orders matching a filter.",
           OrderFilterJsonContext.Default.Options));
```

### A generic method is marked as a tool (TRC0004)

`[TraconTool]` was put on a generic method. A tool call carries a name and a
JSON argument object; there is no call syntax that supplies a type argument, so the
generator has nothing to generate. Write a concrete, non-generic wrapper method and
mark that one instead.

### AddGeneratedTools() finds nothing to register (TRC0005)

`AddGeneratedTools()` was called, but this compilation has no method marked with
`[TraconTool]`. Either the mark was forgotten on the method meant to become a
tool, or the call is left over from a tool set that was since removed. Mark a
method, or remove the call.

### A tool has no description (TRC0006)

A model chooses which tool to call from its name and description; a tool with no
description gives the model only the name and the parameter schema to decide with,
which is not enough for names that are not entirely self-explanatory. Give a
description:

```csharp
[TraconTool("get_order_status", "Returns an order's current shipping status.")]
public static string GetOrderStatus(string orderId) => "shipped";
```

### A complex tool result has no JSON context (TRC0008)

Generated tools return complex results as canonical JSON. Declare a
source-generated `JsonSerializerContext` in your own source and give its type
to the tool attribute. A context emitted by the Tracon generator itself is
too late for the JSON generator to process.

```csharp
[JsonSerializable(typeof(OrderPreview))]
internal partial class ToolJsonContext : JsonSerializerContext;

[TraconTool(
    "preview_order",
    "Returns an order preview.",
    JsonSerializerContext = typeof(ToolJsonContext))]
public static OrderPreview PreviewOrder(string orderId) => new(orderId, "ready");
```

### An instance method is marked as a tool (TRC0007)

Generated tools must be static. Microsoft Agent Framework invokes an
`AIFunction` with an empty service provider, so an instance method cannot rely
on constructor dependencies. There are three ways out, and the diagnostic names
all three: make the generated method static, create the object during
registration and hand over a built function with
`AddTool(AIFunctionFactory.Create(...))`, or — when the dependency has to be
resolved per call — register it with `AddScopedTool(...)`, which opens a scope
for each invocation:

```csharp
builder.AddTracon()
       .AddScopedTool(AIFunctionFactory.Create(
           async (string orderId, AIFunctionArguments arguments) =>
           {
               var orders = arguments.Services!.GetRequiredService<IOrderRepository>();
               return await orders.GetAsync(orderId);
           },
           "look_up_order",
           "Looks an order up by its identifier."));
```

### A tool parameter has no description (TRC0009)

The model fills in a tool call's arguments from the JSON Schema the generator
produces; a parameter's `description` is the strongest signal it has for which
argument to put where and what value belongs in it — stronger than the parameter
name, which a model reads but was never written for this purpose. `System.ComponentModel.DescriptionAttribute`
reaches the schema:

```csharp
[TraconTool("submit_order", "Submits an order to the fulfillment system.")]
public static Task<OrderReceipt> SubmitOrderAsync(
    [Description("The identifier of the order to submit.")] string orderId,
    CancellationToken cancellationToken)
```

This is a warning, not an error — existing code keeps compiling. `AIFunctionFactory.Create`
reads the same attribute, so a tool written either way teaches the model the same
way. `CancellationToken` never needs one: it never reaches the schema.

### A parameter constraint does not apply (TRC0010)

A `System.ComponentModel.DataAnnotations` constraint attribute was found on a
parameter whose type or shape it does not support — `[Range]` on a `string`, a
length constraint on a `bool`, or `[Range(typeof(decimal), "0", "1")]` (the
`Type`-based overload never gives the generator a compile-time constant). The
constraint is silently left out of the generated schema instead of producing a
wrong one:

```csharp
// TRC0010: [Range] does not apply to string.
public static void SetCode([Range(1, 10)] string code) { }
```

This is a warning, not an error — existing code keeps compiling, minus the one
constraint. Remove the attribute, or register the tool by hand instead of through
`[TraconTool]`:

```csharp
builder.AddTracon()
       .AddTool(AIFunctionFactory.Create(MyMethod));
```

### An object parameter references a type missing from the JSON context (TRC0011)

A tool has an object parameter (or a parameter whose type contains a nested
object), and the type in the message is not declared with `[JsonSerializable]`
on the `JsonSerializerContext` that `TraconTool.JsonSerializerContext` points
at. The generator never emits its own context for a nested type — the same rule
`TRC0008` enforces for a complex result: binding a nested object requires its
metadata, and that metadata comes only from a context the tool owner wrote.

Declare every type in the object graph — the parameter's own type and any type it
nests — with its own `[JsonSerializable]`:

```csharp
public sealed record Criterion(string Name, int Weight);
public sealed record Rubric(string Title, IReadOnlyList<Criterion> Criteria);

// TRC0011 until BOTH types are declared - Rubric nests Criterion.
[JsonSerializable(typeof(Rubric))]
[JsonSerializable(typeof(Criterion))]
internal partial class ToolJsonContext : JsonSerializerContext;

[TraconTool("score_submission", "Scores a submission against a rubric.", JsonSerializerContext = typeof(ToolJsonContext))]
public static string ScoreSubmission(Rubric rubric) => "scored";
```

Every distinct missing type is reported once, in the same compilation, so a
consumer can add them all rather than discovering them one build at a time.

### An object parameter's graph is too deep or cyclic (TRC0012)

A supported object parameter (`TRC0003`) nests another object more than 3 levels
deep, or a type reaches itself again through its own members — the message names
the path (`A → B → A`). Both are rejected at compile time instead of risking a
generator that recurses forever, or a schema the model's own error rate rises
against once it gets this deep.

Flatten the type so it needs fewer nested levels, break the cycle, or register
the tool by hand instead of through `[TraconTool]`:

```csharp
builder.AddTracon()
       .AddTool(AIFunctionFactory.Create(MyMethod));
```

### TRC0101 or TRC0102 fires although the registration exists

An analyzer sees a single compilation. When `AddTracon()` or a provider
registration lives in another assembly, the diagnostic cannot see it. Turn the
whole usage family off with one property:

```xml
<PropertyGroup>
  <TraconUsageDiagnostics>false</TraconUsageDiagnostics>
</PropertyGroup>
```

To keep the rest, silence one rule in `.editorconfig` instead:

```ini
[*.cs]
dotnet_diagnostic.TRC0101.severity = none
```

The `Tracon.Tools` family is unaffected by either switch; those diagnostics
report a tool that cannot be generated at all.

### AGENTS.md does not appear

Writing it is opt-in, so that adding a package reference never changes files in
your repository:

```xml
<PropertyGroup>
  <TraconWriteAgentsFile>true</TraconWriteAgentsFile>
</PropertyGroup>
```

The next build writes the capability map to `AGENTS.md` at the root of the
repository, next to the project when there is no repository. A project created
with `dotnet new tracon-api` sets the property already.

### AGENTS.md is out of date (TRC0401)

An existing file is never overwritten, because you may have added notes to it.
Refresh it by deleting it and building again:

```bash
rm AGENTS.md && dotnet build
```

The same map is published for web-based agents at
[`/llms.txt`](/llms.txt), followed by one line per
documentation page, with every hand-written page concatenated at
[`/llms-full.txt`](/llms-full.txt).

### I keep my own AGENTS.md, so the map never arrives (TRC0402)

Expected: your file is never overwritten. The map still ships inside the package,
and `Tracon.LocalReference.md` carries its absolute path on this machine. Ask
for that file — it lands beside each project and leaves your repository root alone:

```xml
<PropertyGroup>
  <TraconWriteLocalReference>true</TraconWriteLocalReference>
</PropertyGroup>
```

Then point your own file at it, in one line:

```markdown
Tracon: read Tracon.LocalReference.md beside each project for the capability
map and the API documentation of the installed version.
```

`TRC0402` looks for that file name anywhere in `AGENTS.md` and goes quiet once it is
there. It stays silent in two other cases as well: while the property above is off,
because then there is no file to name, and on a file this package generated, because
a generated map already names it.

### The gate skill is out of date (TRC0403)

`tracon agent-skill` stamps the file it writes with the capability map revision
the tool was built from, and the build compares that against the map your
installed packages ship.

The tool carries the revision it stamps, so update the tool first — re-running an
older one writes the same stale value back:

```bash
dotnet tool update -g Tracon.Cli
rm .claude/skills/tracon/SKILL.md
tracon agent-skill
```

Deleting is the refresh step because the file is never overwritten in place: you
may have added notes to it. A `SKILL.md` without that stamp is treated as yours
and is never reported.

### An ambient write does not survive a streaming loop (TRC0501)

`TraconRunContext.SetCurrent`, `AmbientTenantScope.Begin`, `AmbientRunAttributionScope.Begin`,
and `ActivitySource.StartActivity` all write through an `AsyncLocal<T>`. An assignment
made inside an `async` iterator's body does not cross a `yield return`: the driver
restores the execution context, and the next step of the loop starts with a null or
stale scope, so a nested call inside it reads the wrong tenant, run, or span.

```csharp
// Wrong: the write happens once, before the loop starts.
TraconRunContext.SetCurrent(scope);

while (true)
{
    if (!await enumerator.MoveNextAsync())
    {
        break;
    }

    yield return enumerator.Current;
}
```

```csharp
// Right: the write repeats immediately before every step that advances the
// enumeration.
while (true)
{
    TraconRunContext.SetCurrent(scope);

    if (!await enumerator.MoveNextAsync())
    {
        break;
    }

    yield return enumerator.Current;
}
```

The diagnostic is reported on the loop, not on the write: move the assignment it
names into the loop the warning points at, immediately before the call that advances
the enumeration.

### A `Begin(...)` scope is opened without being restored (TRC0502)

`AmbientTenantScope.Begin(...)` and `AmbientRunAttributionScope.Begin(...)` return an
`IDisposable` for exactly one reason: disposing it restores the ambient value that was
there before the scope opened. A call whose result is never assigned anywhere never
restores it, and the value it set stays visible for the rest of the current execution
context.

```csharp
// Wrong: the scope is never restored.
AmbientTenantScope.Begin(tenantId);

// Right.
using var scope = AmbientTenantScope.Begin(tenantId);
```

### The agent knows a capability exists but not how to call it

The map names every entry point; it explains none of them. The explanation is
already on your disk, and `Tracon.LocalReference.md` names where. The same
property writes it, beside each project that references Tracon:

```xml
<PropertyGroup>
  <TraconWriteAgentsFile>true</TraconWriteAgentsFile>
</PropertyGroup>
```

The file lists one XML documentation file per referenced package, and — when you
reference `Tracon.AspNetCore` — the OpenAPI document that describes the HTTP
surface. Every entry point in those files carries a worked example, so an agent
answers a call-shape question with a search rather than a guess:

```bash
grep -A 12 'AddToolApprovalPolicy' <api-doc>
```

One file is written per project, not one for the repository: a solution that
splits a web host from a worker gives each project a different set of packages,
and only the web host's file names the HTTP API document. The paths are specific
to your machine and to the versions that project restored, so the file is
regenerated on every build and belongs in `.gitignore`; a project created with
`dotnet new tracon-api` already ignores it. To write the map but not the
pointer file, set `TraconWriteLocalReference` to `false`.

## Read next

- [Observability and cost](/guides/observability/) — the traces and metrics that answer a question before it becomes a symptom
- [Reliable runs](/guides/reliability/) — the failure boundaries that stop several of these symptoms recurring
- [Configuration](/reference/configuration/) — the option behind most of the fixes above
