---
title: Embedding into a host application
description: Bind Tracon's seven embedding points to your own identity, authorization, eventing, storage, and approval presentation, and read the identity a tool body sees.
---

`AddTracon()` plus `MapTracon()` is a complete, working setup on its own;
[Your first agent](/getting-started/first-agent/) starts from it. Embedding Tracon into an application that already has its own tenants,
users, permissions, event bus, or object storage is a different job: it means
replacing built-in defaults with bindings into systems you already run. There are
seven embedding points. The six below are wired the same way, are all optional, and
can be added one at a time; the seventh, `IToolApprovalPresenter`, is covered with
[Approvals](/concepts/governance/#approvals).

## The six points

| Contract | What you give it | Built-in default when unbound |
|---|---|---|
| `ITenantContext`, `ITenantStore` | The current tenant, resolved from your own identity layer | A single fixed tenant |
| `IRunAttributionContext` | The current user and job labels a run belongs to | `UserId` and `Labels` are always `null` |
| `IToolAuthorizationHandler` | A decision for every tool call that runs through the tool chain ([which tools](#which-tools-reach-the-tool-authorization-handler)): allowed or denied | Every call is allowed |
| `IRunAuthorizationHandler` | A decision for every run start, every access to a run's resources, and every session access: allowed or denied | Every run starts, every run is readable, and every session is reachable |
| `IRunEventSink` | A bridge that receives every run event as it is written | No bridge; events reach only `IRunStore` |
| `IAttachmentStorage` | A place to write attachment bytes outside the database | Content is stored as `bytea` in the database |

Each interface is registered with `TryAdd`, so a registration made **before**
`AddTracon()` wins over the built-in default. A registration made after it is
where module order starts to matter: a `TryAdd` registration is dropped, because
Tracon's default already holds the slot, while a plain `Add` still wins the
resolve and leaves Tracon's unused registration behind it. Register first and
neither case can bite you. `GET /api/diagnostics` (once you turn it on) reports which
of the six are still built-in and which your application replaced — see
[Extension points](#extension-points-in-diagnostics) below; to turn a missed binding
into a failed startup instead of a report nobody reads, see
[Make a binding required](#make-a-binding-required).

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ITenantContext, YourTenantContext>();
builder.Services.AddSingleton<ITenantStore, YourTenantStore>();
builder.Services.AddSingleton<IRunAttributionContext, YourRunAttributionContext>();
builder.Services.AddSingleton<IToolAuthorizationHandler, YourToolAuthorizationHandler>();
builder.Services.AddSingleton<IRunAuthorizationHandler, YourRunAuthorizationHandler>();
builder.Services.AddSingleton<IRunEventSink, YourRunEventSink>();
builder.Services.AddSingleton<IAttachmentStorage, YourAttachmentStorage>();

var tracon = builder.AddTracon()
    .UseOpenAI(builder.Configuration.GetSection(OpenAIProviderOptions.SectionName))
    .UsePostgreSql(builder.Configuration.GetSection(TraconPostgreSqlOptions.SectionName));
```

`UsePostgreSql` comes from the `Tracon.PostgreSql` package, which the `Tracon` meta package does not include: `dotnet add package Tracon.PostgreSql --prerelease`.

The order above — bindings first, `AddTracon()` second — is the only order that
works. `AddTracon()` calls `TryAdd*` for all six; called first, it claims every
slot and your registrations that follow do nothing.

### A host that gives you only `IServiceCollection`

Some module systems hand each module an `IServiceCollection` and no host builder.
Call the `IServiceCollection` overload there, and pass it the `Tracon` section
itself, not the configuration root:

```csharp
services.AddTracon(configuration.GetSection(TraconOptions.SectionName));
```

`builder.AddTracon()` passes that section for you. `services.AddTracon()` with no
argument reads **no** configuration: every `Tracon:*` setting in `appsettings.json`
or the environment is ignored, and each option keeps its default until code changes
it. The provider and store calls that follow take their own sections, as above.

### 1 — Tenant resolution

```csharp
public sealed class YourTenantContext(IYourIdentityService identity) : ITenantContext
{
    public string TenantId => identity.CurrentTenantId;
}
```

The value you return is a tenant identifier: the ASCII letters, the digits, `_`, `.` and `-`, and
nothing else. Every store refuses any other character with an `ArgumentException` on the first
call, so a context that takes the value from a client's text (a header, a claim, a subdomain)
checks it first with `AmbientTenantScope.TryNormalize` and answers the client with `400`; it must
not let the request reach a store. The reason is the database: an engine that compares text by a
collation can join two other spellings (an accented letter composed and decomposed, a full-width
letter) into one tenant, and the idempotency keys, API keys and runs of two tenants would be
shared. The same applies to the tenant a job carries into `AmbientTenantScope.Begin`. A context
that fails closed on a value it cannot vouch for returns `Vouch(identity.CurrentTenantId)` from
`TenantId`, with this check:

```csharp
private static string Vouch(string? tenantFromTheIdentityLayer)
    => AmbientTenantScope.TryNormalize(tenantFromTheIdentityLayer, out var tenant)
        ? tenant
        : throw new InvalidOperationException("The identity layer returned a tenant that is not an identifier.");
```

`YourTenantContext` replaces the built-in resolver entirely — the claim/header
resolution chain [Governance](/concepts/governance/#multi-tenancy) describes only
applies when you leave the default in place and turn it on with `UseTenancy()`.
Bind your own `ITenantContext` instead when the tenant already lives in a service,
a claim shape, or a header your identity layer owns. Bind `ITenantStore` alongside
it only if you also want Tracon's tenant admin endpoints
(`/api/tenants`) to read and write your own tenant records instead of its
in-memory default.

For work that runs outside an HTTP request — a queued job, a scheduled task — no
`HttpContext` exists for `ITenantContext` to read. Open an ambient scope for the
duration of that work instead. It composes with
[`AmbientRunAttributionScope`](/concepts/starting-runs/#who-ran-it-and-for-what) — a
background job binds both together, since neither has a request to read from:

```csharp
using (AmbientTenantScope.Begin(job.TenantId))
using (AmbientRunAttributionScope.Begin(job.RequestedByUserId, labels: null))
{
    await agent.RunAsync(job.Message);
}
```

**Open the scope inside the method that starts the run**, in that method's own
body — not in a helper it calls — and reopen it before every `MoveNextAsync` on a
streaming path. `AmbientTenantScope` is an `AsyncLocal<T>`; a value set upstream of
an `await` boundary does not flow back down through one opened later. A dropped
scope reads as "empty tenant", not as an exception, so it fails silently.

### 2 — Run attribution

Attributes a run to a user and, optionally, job labels — see
[Starting runs: who ran it and for what](/concepts/starting-runs/#who-ran-it-and-for-what) for the
full contract, including why the value is never read from the run request body.

### 3 — Tool authorization

Decides whether a caller may invoke a specific tool at all, separately from
approval — see [Tools](/concepts/tools/#authorization-validation-and-timeout) for the binding
pattern and how authorization and approval order relative to each other. If your
widget renders its own approval card instead of the built-in console's, register
[`IToolApprovalPresenter`](/concepts/governance/#approvals) too — it turns a raw
`{ "orderId": "ORD-1001" }` into a name your widget can show directly, and it reaches
your widget the same way the built-in one reads it: an `approvals` frame on the
streaming run endpoint, keyed by the pending request's `requestId`. A widget that shows
the streamed reply of an agent with a [loop](/concepts/agents/#run-until-the-work-is-done)
needs one more frame: `loop.iteration-completed` marks where an iteration ends, because
nothing in the `update` frames does. The [embeddable chat widget](/ui/#embeddable-chat-widget)
does not stream (it sends `Idempotency-Key`), so it gets the final iteration only.

#### Which tools reach the tool authorization handler

The handler is asked for every tool call that runs through Tracon's tool chain:

- tools you register in code (`AddTool`, `AddToolsFrom`, `AddScopedTool`) and the built-in tools
  `speak`, `transcribe` and `generate_image`;
- the tools of the MCP servers of the tenant;
- the `file_access_*` tools of a [session workspace](/guides/context-and-memory/#give-an-agent-files-in-each-session),
  with the effect `Read`, `Write` or `Destructive`;
- `search_knowledge`, the search tool of a [knowledge base](/guides/knowledge/), and
  `search_conversation_memory`, the on-demand search of
  [conversation memory](/guides/context-and-memory/#remember-a-user-across-sessions), both with the effect `External`:
  the text of the query leaves the process for the embedding provider and the vector store.

It is not asked for a tool that Tracon does not run or does not own: a hosted tool (it runs at the model provider), an
agent called as a tool, the skill tools, the file memory, todo and harness tools, and the tools a provider
that an agent extension adds brings with it. The two search tools are not in the tool registry, so `GET /api/tools` does
not list them: write their names in your policy as fixed text.

The same list is what a tenant's [tool policy](/concepts/tools/#per-tenant-tool-policy) covers: the policy is
checked first, and a call it refuses is not shown to your handler.

### 4 — Run event bridge

Bridges every run event to your own queue or bus, in addition to the run store —
see [Runs: observing events beyond the store](/concepts/runs/#observing-events-beyond-the-store)
and [Troubleshooting a slow sink](/guides/observability/#troubleshooting) for the
full contract.

**The one rule that matters most:** `OnEventAsync` must queue the event and
return. Tracon awaits it directly on the run's own hot path, before the
response keeps streaming to its caller — a sink that does its own network I/O
inline ties the model's response speed to that network call's latency.

Tracon holds **no queue of its own** in front of your sink, so the buffer is
yours to own: write to a bounded channel and return. Size that channel to drop
the event and log it when it is full rather than block, so a slow consumer of
yours never slows the run down to match its queue depth.

### 5 — Attachment storage

```csharp
public sealed class YourAttachmentStorage(IYourBlobClient blobs) : IAttachmentStorage
{
    public async ValueTask<Uri> WriteAsync(
        string tenantId, Guid id, Stream content, string mediaType, CancellationToken cancellationToken = default)
        => await blobs.PutAsync($"{tenantId}/{id}", content, mediaType, cancellationToken);

    public ValueTask<Stream?> ReadAsync(Uri uri, CancellationToken cancellationToken = default)
        => blobs.OpenReadAsync(uri, cancellationToken);

    public ValueTask DeleteAsync(Uri uri, CancellationToken cancellationToken = default)
        => blobs.DeleteAsync(uri, cancellationToken);
}
```

`IAttachmentStore` keeps the metadata row either way; `IAttachmentStorage` only
decides where the bytes live. Tracon takes no dependency on any cloud SDK —
you write this class against whichever client your object store already uses.

### 6 — Run and session authorization

Tracon draws ownership at the **tenant** level by default; without this
binding, every caller with the `Operator` role in a tenant can start a run as,
read, cancel, score, and delete every other user's runs, attachments,
approvals, and sessions in the same tenant.

:::note[Sessions have a built-in answer too]
Turning on [session ownership](/concepts/sessions/#session-ownership) makes
Tracon record which user opened a session and narrow the session list to
that user, with no handler at all. It covers **sessions only**; runs,
attachments, approvals, and scores still need the handler below. The two
compose — with ownership on, a handler no longer has to reject a whole session
listing just to keep users apart.
:::

#### Ownership and attribution are not the same promise

Both read the user from `IRunAttributionContext`, and it is worth being exact
about how differently they treat a failure there:

| | Attribution | Session ownership |
|---|---|---|
| What the value does | Names the user on a cost report | Decides who may reach a session |
| If your implementation throws | The run continues; the column stays `NULL` | The session is not opened: `403` |
| If it returns an over-long value | Dropped whole; the run continues | Treated as no identity: `403` |
| When it is read | Every run | Only when a session is **opened** |

Same service, two contract strengths. The rule "observability never breaks
functionality" holds for attribution and deliberately does **not** hold once a
deployment has asked for that value to be an authorization input. If you bind
`IRunAttributionContext` and later turn ownership on, re-read your
implementation with that in mind: a path that used to degrade quietly now
refuses.

`AuthorizeRunAsync` answers two different questions, told apart by
`request.Access`. `RunAccess.Start` asks whether a run may **begin**;
every other value asks whether an existing run's **resource** may be reached,
and carries `request.RunId` so you can look that run up in your own records:

| `RunAccess` | What the caller is asking to do |
|---|---|
| `Start` | Start a run — including a replay, which carries the source run's `RunId` |
| `Read` | Read the run: summary, tree, event stream, recorded input, span tree, tool calls, scores |
| `Cancel` | Request cancellation of the run |
| `Feedback` | Write or delete a score for the run |
| `Attachment` | Upload, download, list, or delete an attachment |
| `Approval` | List, read, or decide an approval request |

```csharp
public sealed class YourRunAuthorizationHandler(IYourOwnershipService ownership) : IRunAuthorizationHandler
{
    public async ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
        RunAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        // Starting a run: there is no run yet, so the question is about the agent.
        if (request.Access == RunAccess.Start && request.RunId is null)
        {
            return await ownership.CanStartAsync(request.TenantId, request.UserId, request.AgentName!, cancellationToken)
                ? RunAuthorizationResult.Allow()
                : RunAuthorizationResult.Deny("This user cannot run this agent.");
        }

        // Everything else is about an existing run. RunId is null only for a
        // list, and for an attachment uploaded before any run existed.
        if (request.RunId is not { } runId)
        {
            return RunAuthorizationResult.Allow();
        }

        return await ownership.OwnsRunAsync(request.TenantId, request.UserId, runId, cancellationToken)
            ? RunAuthorizationResult.Allow()
            : RunAuthorizationResult.Deny("This run belongs to a different user.");
    }

    public async ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(
        SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        // request.SessionId is null only for SessionAccess.List, which has no
        // single session identity to check ownership of.
        if (request.Access == SessionAccess.List)
        {
            return RunAuthorizationResult.Allow();
        }

        return await ownership.OwnsAsync(request.TenantId, request.UserId, request.SessionId!, cancellationToken)
            ? RunAuthorizationResult.Allow()
            : RunAuthorizationResult.Deny("This session belongs to a different user.");
    }
}
```

Both methods are called explicitly at every endpoint concerned — there is no
single filter they all share, because these endpoints have no route shape in
common, so each one calls this binding in its own body:

- **Six run-starting endpoints:** the agent run endpoint, the workflow run
  endpoint, the inbound trigger accept endpoint, the OpenAI-compatible
  `/v1/responses` and `/v1/chat/completions` endpoints, and `POST
  /api/runs/{id}/replay`. The check runs **before** the quota check, so a
  denied call never consumes the tenant's quota.
- **The two external protocols:** every message an
  [A2A](/guides/external-agents/#publish-agents-through-a2a) agent receives
  (`SendMessage` and `SendStreamingMessage`) and every
  [MCP](/guides/external-agents/#publish-agents-as-mcp-tools) `tools/call` that names an exposed agent. The
  handler gets the same question as a run start, with `request.SessionId` set to the
  conversation's identity for A2A and `null` for MCP, which keeps no session. A
  refused call starts no run, calls no model and opens no run row. An MCP call that
  asks for a task is refused before the task exists. How the refusal reaches the
  caller is told on the external agents page.
- **Every run resource:** the run summary, its tree, its event stream, its
  recorded input, its span tree, its tool calls, its scores, its
  cancellation, its attachments, and its approval requests.
- **Every session access:** list, read, delete, branch, and opening a
  real-time voice conversation (`SessionAccess.Voice`) — including the
  OpenAI-compatible routes that reach the same sessions under another name,
  `GET`/`DELETE /v1/conversations/{id}` and `GET /v1/conversations/{id}/items`.
  `POST /v1/conversations` is not among them: it reserves an identifier and
  writes nothing, so there is no session yet to authorize.

**How a denial answers depends on what was asked for.** A denied single
resource — a run, a session, an attachment, an approval request — returns
`404`, with a body **identical** to the one that resource gets when it
genuinely does not exist. A `403` there would confirm the resource exists to
a caller who should not even know it, and wording that differed between
"denied" and "missing" would leak the same thing through a side channel. A
denied **list** returns `403` instead: a list is an operation, not a single
resource, so there is no existence to leak, and the response is never
silently filtered — filtering there would break the `skip`/`take` paging
contract. A denied **run start** and a denied **attachment upload** also
return `403`: neither addresses an existing resource. A denied voice
handshake is refused before the socket upgrades, with the same `404` an
unreachable session already gives.

A voice session that does not exist yet is **not** an error: the first turn
opens it, and the handler is still asked, so you decide for yourself whether
a caller may open a conversation under that id.

If this handler throws, the call is denied (fail-closed); a gate that fails
open on an exception is not a gate. When it denies a single resource, the
`Reason` you supply is deliberately **not** returned — the response has to
stay identical to a missing resource's.

A throw is a failed check, not a decision, and Tracon reports it that way:

- It writes one `Error` log line in the `Tracon.RunAuthorization` category.
  The line names your handler type, the access kind and the tenant, agent, run
  and session ids, and carries the exception. The exception text never goes
  into the HTTP response.
- A `403` (a run start, a list, a replay, a workflow resume or respond) says
  *The run authorization check failed. Retry the request.* instead of claiming
  that your handler denied the call. A `404` does not change: it stays
  identical to a missing resource's.
- An `OperationCanceledException` that the request itself did not cause (for
  example, an `HttpClient` timeout inside your handler) is a failure like any
  other and is denied. Only the caller's own cancellation travels on.

If your `IRunAttributionContext` throws while a request is authorized, the
caller is treated as having no identity, and one `Error` line per request is
written in the same category.

## Reading identity inside a tool body

A tool cannot reach `AgentSession`, so it cannot read `ITenantContext` or
`IRunAttributionContext` through the normal request pipeline. It reads the same
values from `TraconRunContext.Current` instead — a static, `AsyncLocal`-backed
snapshot the run pipeline populates before every tool call:

```csharp
[TraconTool("current_account", "Returns the tenant, run, session, and caller identity of the current run.")]
public static string CurrentAccount()
{
    var scope = TraconRunContext.Current;

    return scope is null
        ? "no run in progress"
        : $"tenant={scope.TenantId} run={scope.RunId} session={scope.SessionId} user={scope.UserId}";
}
```

This is the only place a tool can read the run's identity — there is no parameter
Tracon injects for it. `scope.UserId` is the same value `IRunAttributionContext`
resolved for the run record, not a new concept — just a second place to read it
from. `AgentRunScope` also carries `RootRunId` (the top of an
agent-calls-agent tree) and `Budget` (the shared token/depth/count ceiling for that
tree).

### Your own tenant scope does not follow a queued run

A queued or scheduled run starts on Tracon's job worker, with no request. The worker opens
`AmbientTenantScope` for the job's tenant, so `TraconRunContext.Current.TenantId` is that tenant.
Your application's own ambient tenant does not change: a framework service that a request middleware
sets still holds what the worker has, which is no tenant. A tool that reads your own tenant-filtered data
then reads the rows that belong to no tenant, and nothing says so.

`IHostTenantScope` closes the gap in one place. Register it, and the worker calls it for every job, right
after it opens its own scope and before it builds the container scope the job runs in. It disposes what
you return when the job ends. The handler, the agent's tools and decorators, and the sub-agents it calls
all run inside your scope:

```csharp
public sealed class AcmeTenantScope(Func<string, IDisposable> changeCurrentTenant) : IHostTenantScope
{
    // Your framework's own call; the identifier is Tracon's (lower case), mapped to your key here.
    public IDisposable? Begin(string tenantId) => changeCurrentTenant(tenantId);
}
```

Register it with `services.AddSingleton<IHostTenantScope, AcmeTenantScope>()`. There is no default:
without a registration the worker calls nothing. The implementation is a singleton and is called
concurrently, once for each job, so it must not hold a scoped service. `Begin` is synchronous on purpose:
a value you write to an `AsyncLocal` inside it reaches the job, and an asynchronous method would lose it.

If `Begin` throws, the job fails before it runs, with the code `tracon.job.host-tenant-scope-failed`, and it is
not tried again: an application that does not know the tenant will not know it on the next attempt.
If `Dispose` throws, the error is logged and the job's result stays as it was. Only the job worker
calls it: every other path that runs a tool (a request, a voice session, an A2A call) starts from a request,
where your middleware has already opened your scope. A tool that must also work on a host that did not
register the interface can still open the framework's scope itself from `TraconRunContext.Current.TenantId`,
in its own body.

### Start an agent run from your own code, with values

Your host's own background worker or API can run a catalog agent without an HTTP call. When the agent's
instructions name a value the run needs (`{{articleContent}}`), pass the value to the resolve:

```csharp
var catalog = provider.GetRequiredService<IAgentCatalog>();
var articleText = "Tides follow the moon.";

var summarizer = await catalog.ResolveAsync(
    "summarizer",
    new AgentResolveOptions
    {
        Parameters = new Dictionary<string, string> { ["articleContent"] = articleText },
    });

await summarizer!.RunAsync("Summarize it.");
```

The catalog checks the values against the agent's schema, compiles the agent for exactly those
values, and still applies every decorator, so the run is recorded, governed and replayable with the
same text. Open your own tenant and attribution scope around the call, in the worker's own body,
as above. [Give a value from .NET code](/concepts/agents/#give-a-value-from-net-code) lists what
`TraconParameterException` carries. `samples/Tracon.Embedded` runs this from its job worker.

### Check a definition before you save it

A host that writes definitions from its own code (a seed, a migration, an import tool) calls
`IAgentDefinitionStore.SaveAsync`. The store keeps what it is given. It does not compile the
definition, so one that cannot be compiled (an Anthropic model with a `Temperature`, a tool nobody
registered) is saved, and the first call that resolves the agent fails. The HTTP writers and the
import check a definition before they save it. `IAgentDefinitionValidator` is the same check, for
your own code:

```csharp
var validator = provider.GetRequiredService<IAgentDefinitionValidator>();
var store = provider.GetRequiredService<IAgentDefinitionStore>();

var definition = new AgentDefinition
{
    Name = "support",
    Instructions = "Answer questions about orders.",
    Model = new ModelBinding { Provider = "anthropic", Model = "claude-sonnet-5-5", Temperature = 0.2f },
};

var report = await validator.ValidateAsync(definition);

if (report.Valid)
{
    await store.SaveAsync(definition);
}
else
{
    foreach (var message in report.Messages)
    {
        if (message.Severity == ValidationSeverity.Error)
        {
            Console.Error.WriteLine($"{message.Code} at {message.Path}: {message.Message}");
        }
    }
}
```

The report lists every finding, not the first. A finding with `Warning` severity leaves `Valid` true;
read it too, for example `single_brace_placeholder` when the instructions write `{customer}` and the
agent declares a parameter `customer` (the placeholder is `{{customer}}`). The report never calls a
model and writes nothing. It reads the model credential and the egress policy of the ambient tenant, so
call it inside the tenant the definition belongs to. The codes are the ones `POST /api/agents/validate`
returns and never change inside a major version.

## Two data planes, one connection pool or two

Your application's own schema and Tracon's tables can live in the same
PostgreSQL database. Tracon writes only inside its own schema (`SchemaName`,
default `tracon`; see
[Choose a migration strategy](/guides/production/#choose-a-migration-strategy)) and
never reads or writes yours.

**Giving both sides the same connection string does not share a pool.** Npgsql
pools a `NpgsqlDataSource` **instance**, not a connection string — two separate
`NpgsqlDataSource` objects built from an identical string open two separate
pools (measured: with 5 concurrent commands held open on each of two data
sources built from the same string, the server showed 10 simultaneous
backends, not 5). If your application uses Entity Framework Core (or any other
Npgsql consumer) and you want Tracon sharing its actual pool, build **one**
`NpgsqlDataSource` and give the same instance to both sides:

```csharp
var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
builder.Services.AddDbContext<YourDbContext>(o => o.UseNpgsql(dataSource));
builder.AddTracon()
       .UsePostgreSql(o => o.DataSource = dataSource);
```

See [Two connection planes: EF Core and Tracon](/guides/ef-core/) for the
full pattern, including startup/shutdown ownership. Without a shared
`DataSource`, pointing `Tracon:PostgreSql:ConnectionString` at the same
string your application uses is still fine — the two sides simply keep
independent pools against the same database, exactly as if they pointed at two
different databases.

Give Tracon a **separate** connection string (or data source) — same
server, different database, or a fully different server — when you want its
connection ceiling, credentials, or failure blast radius kept independent of
your application's own database traffic. Nothing in Tracon requires this;
it is purely an operational choice, and it can be changed later since only the
connection string moves.

`AutoApplyMigrations` (default `true`) applies to Tracon's own schema only. In
an embedded setup where your application already owns a controlled migration step
for its own schema, set it to `false` and call `ApplyAsync()` on the registered
`IMigrationApplier` from that same step — Tracon's schema then
migrates alongside yours instead of at every instance's startup. See
[Choose a migration strategy](/guides/production/#choose-a-migration-strategy) for
the fleet-deployment version of this same setting.

## Make a binding required

Reporting a missed binding is not the same as refusing to run without it. An
application that means to enforce its own rule can declare the binding required, and
the host then does not start while Tracon's built-in default is what resolves:

```csharp
builder.Services.AddSingleton<IRunAuthorizationHandler, YourRunAuthorizationHandler>();
builder.Services.AddSingleton<IToolAuthorizationHandler, YourToolAuthorizationHandler>();

builder.AddTracon()
    .RequireCustomBinding<IRunAuthorizationHandler>()
    .RequireCustomBinding<IToolAuthorizationHandler>();
```

The check runs while the host starts, and the message names the contract, the type
that resolved instead, and how to fix it:

```text
IRunAuthorizationHandler was declared as a required custom binding, but Tracon's
built-in default AllowAllRunAuthorizationHandler is what resolved. Register your own
IRunAuthorizationHandler on IServiceCollection BEFORE the AddTracon() call.
```

Four properties are worth knowing before you rely on it:

- **It is off by default.** An application that never calls `RequireCustomBinding`
  behaves exactly as it did before, and the call resolves nothing extra at startup.
- **It is not an HTTP concern.** The check runs at host start, so an embedded host
  that never calls `MapTracon()` gets the same guarantee.
- **`IRunEventSink` and `IAttachmentStorage` are judged by absence.** Tracon
  registers nothing for those two, so "still on the default" means no registration at
  all rather than a particular type.
- **It is a composition gate, not a security proof.** It tells you your
  implementation is the one bound. It cannot tell you that your implementation
  decides correctly — that is what your own tests are for.

Any type that is not one of the seven contracts also stops the host, with a message
listing the seven that are accepted.

## Make a production decision required

`RequireCustomBinding<T>()` covers the bindings. Its sibling covers the settings:
`AddTracon()` also brings every security-sensitive switch up permissive, so a
deployment can reach production having never separated tenants, never decided who
owns a session and never registered a content guard — in silence.

```csharp
builder.AddTracon()
    .RequireProductionProfile(profile => profile
        .Accept(TraconProductionRisk.SingleTenant));
```

These decisions are asked about, and each is either answered by turning the feature on
or accepted by name. This table is the one list; other pages point here rather than
repeat it:

| Risk | What the permissive default means | Answer it with |
|---|---|---|
| `SingleTenant` | Every call resolves to the same default tenant | Bind your own `ITenantContext`, or `UseTenancy(options => options.Enabled = true)` |
| `UnownedSessions` | No session records the user it belongs to | `Tracon:SessionOwnership:Enabled` |
| `UnencryptedContentAtRest` | Prompts, responses and tool arguments are stored as clear text | `AddContentProtection(options => …)` with an active key, or `AddContentProtection()` with `Tracon:ContentProtection:Enabled` |
| `UninspectedContent` | No prompt or response is inspected | `AddPatternContentGuard(...)` or `AddContentGuard<TGuard>()` |
| `UnlimitedRequestRate` | One caller can take the whole model budget | `Tracon:RateLimit:Enabled` |
| `UnboundedRetention` | Nothing is ever deleted | `Tracon:Retention:Enabled`, or a policy per target |
| `UnregisteredRolePolicies` | At least one of the `Tracon.Reader`, `Tracon.Operator` and `Tracon.Admin` policies is not registered, so the endpoints it guards get no role check | Define the three policies in `AddAuthorization(...)` with `TraconPolicies.Reader`, `Operator` and `Admin`, and set `TraconEndpointOptions.RequireRolePolicies` so a missing one stops `MapTracon` too |
| `UncheckedToolCalls` | The built-in tool authorization handler allows every call for every caller | Register your own `IToolAuthorizationHandler`, or set `Tracon:Tools:RequireTenantPolicy` to `true` and save a [tool policy](/concepts/tools/#per-tenant-tool-policy) for each tenant, the default tenant included |
| `UnrestrictedTenantEgress` | A tenant with no egress policy may call every provider | `Tracon:TenantProviders:RequireEgressPolicy`, then save a policy for each tenant |
| `UnverifiedTenantHeader` | The tenant is read from a request header and no claim takes precedence, so a client names the tenant it acts as | Set `ClaimType` in `UseTenancy(...)`, or leave `AllowHeaderResolution` off |
| `UnlimitedTenantWorkflows` | One tenant can take every workflow slot | `Tracon:Workflows:MaxConcurrentRunsPerTenant`. Any value the options validator accepts answers the decision; a value equal to `MaxConcurrentRuns` still lets one tenant take every slot, so size it |
| `UnkeyedAuditChain` | The audit trail's hash chain has no key, so anyone who can write to the audit table can change an entry and recompute every hash after it | Put a key under `Tracon:AuditChainKeys:<id>` and set `Tracon:Audit:ChainKeyId` to `<id>`; see [Sign the audit chain with a key](/guides/audit-chain-key/) |
| `UnauthenticatedLocalOperator` | No `AuthToken` and no authorization policy is configured, so a request with no credential is the local operator with full rights. A reverse proxy on a Unix socket that adds no forwarding header makes a remote caller look local | Set `TraconEndpointOptions.AuthToken`, or `RequireAuthorization(...)` with a policy that authenticates the caller. Accept it only when nothing but this machine can reach the endpoints. An API key does not answer it |
| `UnenforcedQuotaOnStoreFailure` | Quota enforcement is on and `Tracon:Quotas:AllowOnStoreFailure` is `true` (the default), so a run goes ahead when the quota store fails and a tenant past its ceiling is not stopped while the database is down | Set `Tracon:Quotas:AllowOnStoreFailure` to `false` to refuse runs whose quota cannot be read. Accept it only when an outage of the quota store must not stop the service |
| `OrphanedRunsAfterRestart` | The state outlives the process (PostgreSQL, SQL Server or a SQLite file) and run reconciliation is off, so a run that a restart, a deploy or a crash cuts off keeps the status `Running` for ever: the model ignored the cancellation and the process ended first, or the process was killed. It stays counted as running and dilutes the error rate | `Tracon:RunReconciliation:Enabled`. Accept it only when you close such runs by hand |
| `InstanceLocalState` | `Tracon:Deployment:Instances` is above `1` and the store keeps its state in one process (memory, or a SQLite file), so each instance sees its own runs, sessions and job queue | `UsePostgreSql` or `UseSqlServer`, with one database that every instance reaches |
| `UnreconciledRuns` | `Instances` is above `1` and run reconciliation is off, so a cancel that reaches the other instance answers `409` and a run whose instance died stays `Running` | `Tracon:RunReconciliation:Enabled` |
| `UnelectedBackgroundWork` | `Instances` is above `1` and single-executor election is off, so every instance runs the periodic background work at the same time | `Tracon:SingletonExecution:Enabled` |
| `InstanceLocalRateLimit` | `Instances` is above `1` and the request-rate limits are counted in the memory of each process, so the deployment admits them once per instance. The inbound trigger limit is always on, so this is asked while the endpoint limit is off too | `Tracon:RateLimit:Scope` set to `Deployment` |
| `InstanceLocalMcpTokens` | `Instances` is above `1` and the OAuth tokens of MCP servers live in the memory of one process | `Tracon:Mcp:OAuthTokenStore` set to `DistributedCache`, on a cache that every instance shares; or accept it when no MCP server uses OAuth |

Some rows are not asked in every composition:

- `UnrestrictedTenantEgress`, `UnverifiedTenantHeader` and `UnlimitedTenantWorkflows`
  are about one tenant reaching what another should not. They are reported as not
  applicable and never stop a host that does not separate tenants (the built-in
  single-tenant context, or `UseTenancy(options => options.Enabled = false)`). This is
  also why `SingleTenant` and these three are never open together: a host is one or the
  other.
- `UnverifiedTenantHeader` and `UnlimitedTenantWorkflows` exist only when
  `UseTenancy(...)` or `UseWorkflows(...)` is in the chain. `UnlimitedTenantWorkflows`
  is also not asked while `Tracon:Workflows:Enabled` is `false`.
- The five instance decisions (`InstanceLocalState`, `UnreconciledRuns`, `UnelectedBackgroundWork`,
  `InstanceLocalRateLimit` and `InstanceLocalMcpTokens`) are asked only when `Tracon:Deployment:Instances` is above
  `1`. At the default of `1` each is reported as not applicable, so a host that never declared a second instance is
  asked exactly what it was asked before. `InstanceLocalRateLimit` is asked whether or not the endpoint rate limit is on, because the
  inbound trigger limit is always on; `InstanceLocalMcpTokens` exists only when `UseMcp()` is in the chain. See
  [Run more than one instance](/guides/multiple-instances/).
- `UnkeyedAuditChain` is not asked while the audit log is Tracon's in-memory one: that chain lives in the process
  and goes with it, so no one writes to a table behind the host's back. A database audit log, or one you
  registered, is asked.
- `UnregisteredRolePolicies` and `UnauthenticatedLocalOperator` are known only once `MapTracon`
  has run, so they are asked of a host that mapped the endpoints before it started. A host with
  no HTTP surface is not asked them.
- `UnenforcedQuotaOnStoreFailure` is not asked while `Tracon:Quotas:Enabled` is `false`: no run is checked
  against a quota, so no store failure can let one past it.
- `OrphanedRunsAfterRestart` is not asked on the in-memory stores or on a shared in-memory SQLite database
  (`Data Source=file:<name>?mode=memory&cache=shared`), which lose every row with the process, so no
  row is left `Running`. It is not asked when `Tracon:Deployment:Instances` is above `1` either: `UnreconciledRuns`
  asks the same setting there and says the same of a run whose instance died, so one answer covers both and a host
  that already accepted `UnreconciledRuns` is not stopped again. A store you registered yourself is not seen: the
  decision follows the storage provider Tracon registered.

The startup failure names every open decision at once — the setting, what it is
today, and how to answer it — so one restart clears all of them rather than one per
round:

```text
RequireProductionProfile() was called, and 2 production decisions are still on the
permissive default, so the host does not start.

  SingleTenant
    Setting: ITenantContext
    Today:   resolves to Tracon's built-in SingleTenantContext, so every call runs as the default tenant
    Fix:     register your own ITenantContext before the AddTracon() call, or on an ASP.NET Core host call UseTenancy(options => options.Enabled = true)
```

Five properties are worth knowing before you rely on it:

- **It changes no setting.** It sets no value, chooses no policy and turns nothing
  on. The only thing it does is turn a skipped decision into a startup failure.
- **It is off by default.** An application that never calls it behaves exactly as it
  did before, and the call resolves nothing extra at startup.
- **Accepting is per item.** There is no way to accept them all at once, so each
  accepted risk is one reviewable line — and every accepted risk is written to the
  log at information level, by name, each time the host starts.
- **The set of decisions is a versioned contract.** A later release that adds one
  stops a host that calls this method until the new decision is answered or
  accepted. See
  [Compatibility](/reference/compatibility/#the-production-profile-is-a-versioned-contract).
- **It is a composition gate, not a security proof.** It reports that a feature is
  switched on. It cannot tell you the policy behind it is right.

The tenant decision is answered by which `ITenantContext` is bound, so an embedded
host that resolves its tenant from a message header answers it the same way an HTTP
host does. `UseTenancy(options => options.Enabled = false)` is the one case where
that is not enough, and the HTTP package adds its own check for it: two checks can
carry the same risk, and the stricter answer wins. The same seam is open to you —
register your own `IProductionProfileCheck` to add a decision of your own, or to
narrow one of Tracon's.

## Extension points in diagnostics

`GET /api/diagnostics` (off by default; turn it on with
`TraconEndpointOptions.EnableDiagnosticsEndpoint`) reports an `extensionPoints`
array: one entry per contract above, naming the bound implementation's type and
whether it is still Tracon's built-in default.

```mermaid
flowchart TD
    accTitle: Binding and verification order
    accDescr: Register the six implementations, then call AddTracon so TryAdd claims whatever is still unbound, then read the diagnostics endpoint to confirm each binding actually took.
    A["Register ITenantContext, IRunAttributionContext,<br/>IToolAuthorizationHandler, IRunAuthorizationHandler,<br/>IRunEventSink, IAttachmentStorage"] --> B["AddTracon call<br/>TryAdd claims any still-open slot"]
    B --> C["GET /api/diagnostics<br/>reads extensionPoints"]
    C --> D{"isBuiltInDefault?"}
    D -->|false| E["binding is active"]
    D -->|true| F["registered too late, or against<br/>the wrong interface"]
```

```json
{
  "extensionPoints": [
    { "contract": "ITenantContext", "implementation": "YourTenantContext", "isBuiltInDefault": false },
    { "contract": "IRunAttributionContext", "implementation": "DefaultRunAttributionContext", "isBuiltInDefault": true }
  ]
}
```

A fresh installation shows all seven (the six above, plus `IToolApprovalPresenter` —
see [Approvals](/concepts/governance/#approvals)) as built-in. Read this endpoint right after
adding a binding to confirm it actually took — `isBuiltInDefault: true` on a
contract you meant to replace means the registration ran too late, or against the
wrong interface.

## Verification checklist

- [ ] Every binding you need is registered **before** `AddTracon()`
- [ ] Bindings your deployment must not run without are declared with `RequireCustomBinding<T>()`
- [ ] Production decisions your deployment must not skip are declared with `RequireProductionProfile()`
- [ ] `GET /api/diagnostics` shows `isBuiltInDefault: false` for each contract you bound
- [ ] A background job opens `AmbientTenantScope.Begin(tenantId)` in the method that starts the run, and the scope covers every `await` on that path
- [ ] `IRunEventSink.OnEventAsync` never performs blocking I/O inline — it queues and returns
- [ ] `Tracon:PostgreSql:SchemaName` (or the equivalent SQL Server/SQLite setting) does not collide with a schema your own application already owns
- [ ] `AutoApplyMigrations` matches your deployment's migration strategy, not just the default

## Read next

- [Runs](/concepts/runs/) — attribution and the event sink in full
- [Governance](/concepts/governance/) — the built-in tenant resolution chain `ITenantContext` replaces
- [Production](/guides/production/) — migration strategy and process topology
