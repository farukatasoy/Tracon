---
title: Production deployment
description: Deploy Tracon with durable storage, explicit access control, health checks, worker topology, retention, and safe secret handling.
---

A production Tracon deployment needs more than a provider key. It needs durable
state, an explicit identity boundary, a migration strategy, readiness probes, bounded
background work, and a policy for the data that runs create.

The examples below use PostgreSQL. SQL Server is also supported. SQLite is a
single-process option and the in-memory defaults are for development and tests.

## Start from a production registration

Keep credentials outside checked-in configuration. ASP.NET Core maps this environment
variable to `Tracon:PostgreSql:ConnectionString`:

```bash
export Tracon__PostgreSql__ConnectionString='Host=...;Database=...;Username=...;Password=...'
```

Register one provider and one SQL persistence package:

```csharp
using Tracon;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

var tracon = builder.AddTracon()
    .UseOpenAI(builder.Configuration.GetSection(OpenAIProviderOptions.SectionName))
    .UsePostgreSql(
        builder.Configuration.GetSection(TraconPostgreSqlOptions.SectionName));

builder.Services.AddHealthChecks()
    .AddTraconHealthChecks(tags: ["ready"]);

var app = builder.Build();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
});

app.MapTracon("/tracon");
app.Run();
```

A deployment whose module order can leave an authorization handler on Tracon's
permissive default should say so out loud, so the mistake is a failed startup rather
than an allowed request:

```csharp
var tracon = builder.AddTracon()
    .RequireCustomBinding<IRunAuthorizationHandler>()
    .RequireCustomBinding<IToolAuthorizationHandler>();
```

See [Make a binding required](/guides/embedding/#make-a-binding-required) for the
seven contracts this accepts and what the check does and does not prove.

That covers the bindings. The other half of the same argument is the settings:
every row of [Production-sensitive defaults](#production-sensitive-defaults) can
still be skipped in silence. `RequireProductionProfile()` refuses to start a host
that skipped one:

```csharp
var tracon = builder.AddTracon()
    .RequireProductionProfile(profile => profile
        .Accept(TraconProductionRisk.SingleTenant));
```

It changes no setting and secures nothing by itself. It asks about six decisions —
tenant separation, session ownership, at-rest content protection, content
inspection, request rate limiting and retention — and refuses to start while one of
them is still on its permissive default and you have not accepted the risk by name.
The startup failure names every open decision at once, what it is today, and how to
answer it.

Accepting is per item on purpose: there is no way to accept all six at once, so each
accepted risk is one line a reviewer can see. An accepted risk is written to the log
at information level, by name, every time the host starts.

Two things to know before you adopt it. First, the set of decisions is a versioned
contract: a later release that adds one stops a host that calls this method until
the new decision is answered or accepted — that is the method working, and such a
release declares the addition as a behavioural breaking change. Second, like the
binding gate, this is a composition gate and not a security proof: it reports that a
feature is switched on, never that the policy behind it is right.

Use your platform's secret manager for the provider API key and database connection
string. The canonical OpenAI key path is `Tracon:Providers:OpenAI:ApiKey`; its
environment form is `Tracon__Providers__OpenAI__ApiKey`. Do not put either value
in `appsettings.json`, a container image, or a deployment manifest that is not backed
by a secret facility.

## Make the HTTP boundary explicit

Remote access is off by default. In production, connect Tracon to the same
ASP.NET Core authentication scheme that protects the rest of the application. Bind
the three Tracon policy names to your own role or claim model:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("TraconAccess", policy =>
        policy.RequireAuthenticatedUser());

    options.AddPolicy(TraconPolicies.Reader, policy =>
        policy.RequireRole(
            "tracon-reader",
            "tracon-operator",
            "tracon-admin"));

    options.AddPolicy(TraconPolicies.Operator, policy =>
        policy.RequireRole("tracon-operator", "tracon-admin"));

    options.AddPolicy(TraconPolicies.Admin, policy =>
        policy.RequireRole("tracon-admin"));
});
```

After `UseAuthentication()` and `UseAuthorization()`, map the surface with both the
base policy and strict role-policy validation:

```csharp
app.MapTracon("/tracon", options =>
{
    options.AllowRemoteAccess = true;
    options.RequireAuthorization("TraconAccess");
    options.RequireRolePolicies = true;
});
```

`RequireRolePolicies=true` turns a missing Reader, Operator, or Admin policy into a
startup failure. When it is false, an unregistered role policy is skipped. The base
authorization policy, loopback rule, and API-key scopes still apply, but a production
deployment should fail closed on this configuration error.

The optional `AuthToken` is a single static shared secret. It is useful for a private
operator surface, but it has no per-user identity or individual revocation. Prefer an
ASP.NET Core policy for people and tenant-bound, narrowly scoped Tracon API keys
for system clients.

:::caution[Reverse proxies change the network boundary]
The loopback rule sees the address that connects to ASP.NET Core. A local reverse
proxy can make every caller look local. Configure trusted forwarded-header handling
and TLS at the proxy, but use authentication and authorization as the real boundary.
Do not treat `AllowRemoteAccess=false` as sufficient protection behind a proxy.
:::

## Choose a migration strategy

PostgreSQL configuration has these operational controls:

```json
{
  "Tracon": {
    "PostgreSql": {
      "SchemaName": "tracon",
      "AutoApplyMigrations": false,
      "CommandTimeoutSeconds": 30
    }
  }
}
```

`AutoApplyMigrations` defaults to `true`. Startup takes a database lock scoped to the
Tracon schema, validates checksums for applied migrations, and applies each
pending migration in a transaction. A migration failure stops startup.

For a small service, automatic migration is simple and safe. For a fleet, set it to
false and apply migrations from one controlled deployment step before new
application instances become ready: run `tracon migrate`, or call `ApplyAsync()` on
the registered `IMigrationApplier`. With automatic
migration disabled, Tracon opens its schema-ready gate on the assumption that the
external step completed. The health check still reports pending migrations as
unhealthy.

Never edit an applied embedded migration. A checksum mismatch is an integrity error,
not a warning to bypass.

### Alongside your own EF Core migrations

The two schemas are not connected by a foreign key and apply in either order — but
run them with `AutoApplyMigrations` set to `false`, so exactly one mechanism ever
touches the database at deploy time instead of two racing at every instance's
startup:

```bash
dotnet ef database update            # your application's own schema
tracon migrate --provider postgres --connection "$TRACON_CONNECTION"
```

See [Two connection planes: EF Core and Tracon](/guides/ef-core/) for sharing a
connection pool with an EF Core `DbContext` in the same process.

### Storage defaults and limits

| Setting | Default | Limit or consequence |
|---|---:|---|
| PostgreSQL schema | `tracon` | Lowercase unquoted identifier, at most 63 characters |
| `AutoApplyMigrations` | `true` | A failed migration prevents startup |
| `EnableKnowledge` (PostgreSQL) | `false` | Applies the `pgvector`-dependent migration set; needs no extension permission while off |
| `CommandTimeoutSeconds` | `30` | Valid range 0 through 3,600; `0` means unlimited |
| Persistence without `Use*Sql*` | In memory | State disappears on process exit |
| SQL provider count | One | If several are registered, the last wins and diagnostics become degraded |

PostgreSQL is the only Tracon storage provider with vector search. It is opt-in:
`EnableKnowledge` defaults to `false`, so a deployment that never turns it on needs
no `pgvector` extension and no extension-creation permission at all. Turn it on and
install `pgvector` before migrations run only when the deployment includes the
knowledge system.

## Design the process topology

Every process runs the scheduling worker by default. There are two valid shapes:

- **Combined nodes** serve HTTP and execute jobs. This is simple for a small service.
- **Split nodes** set `Scheduling.RunWorker=false` on API nodes and true on worker
  nodes. Worker nodes must register the same SQL store, providers, agents, and job
  handlers as API nodes.

```mermaid
flowchart LR
    accTitle: Combined nodes and split nodes
    accDescr: In the combined shape one process serves HTTP and executes jobs. In the split shape API nodes set RunWorker to false and worker nodes run the queue; both register the same SQL store, providers, agents, and handlers, and the SQL job leases coordinate them.
    subgraph Combined["Combined nodes"]
        C1["Process<br/>HTTP + worker"]
    end
    subgraph Split["Split nodes"]
        A1["API node<br/>Scheduling.RunWorker = false"]
        W1["Worker node<br/>RunWorker = true"]
    end
    C1 --> DB[("SQL store<br/>runs · jobs · leases")]
    A1 --> DB
    W1 --> DB
    DB --- NOTE["Leases coordinate workers.<br/>MaxConcurrentJobs is per process."]
```

SQL job leases coordinate worker nodes. `MaxConcurrentJobs` is per process, so size
the total concurrency against provider rate limits and database capacity. The
singleton-execution guard coordinates periodic services such as health refresh and
orphan reconciliation; the job queue already has its own leases and does not use that
guard.

Direct-run cancellation is registered in the process that owns the live execution.
A cancel request routed to another instance, or sent after the owner restarts, returns
`409`. Use sticky routing or provide a distributed `IRunCancellationRegistry` when
cross-node cancellation is a requirement. Queued-run cancellation is durable because
it uses the shared job store.

Rate limits are per process for the same reason. The endpoint limiter
(`Tracon:RateLimit`) and the inbound trigger limiter both count in the memory
of one instance; Tracon ships no distributed counter. Two instances with a
`PermitLimit` of 100 admit 200 requests per window between them, and a `Tenant`
partition splits each instance's own window rather than a window shared across
the deployment. Size the limit per instance, or put a shared limiter in the
ingress in front of them. A ceiling that must hold for the deployment as a whole
is a quota instead: quotas count in the database, so the instance count does not
change them.

## Add readiness, liveness, and diagnostics

Use the Tracon check for readiness. It is `Unhealthy` when SQL is unreachable or
migrations are pending. It is `Degraded` when storage works but provider health is
not confirmed, a circuit is open, or several persistence providers are registered.
It does not make a paid model completion.

Keep liveness independent of database and provider availability so the orchestrator
does not restart a healthy process during an upstream outage. The application owns
both health routes and their response policy; Tracon only registers the check.

`GET /api/diagnostics` is not mapped by default. If an operations workflow needs it,
set `EnableDiagnosticsEndpoint=true`, require the Admin role, and restrict the route.
It returns configuration-key names and topology, not secret values, but that metadata
is still sensitive.

## Decide what to retain

Run input, streaming message deltas, and tool arguments/results are recorded by
default. They can contain customer data even when sensitive span tags are disabled.
Start with an explicit classification:

| Data | Default recording or retention behavior |
|---|---|
| Run events | Recorded; configuration retention is inactive until retention is enabled |
| Tool payloads | Recorded, with event payloads capped at 8,192 characters |
| Run inputs | Recorded without event-payload truncation so replay remains correct |
| Persisted traces | Successful runs sampled at 10%; failures retained when enabled |
| Sessions and conversations | No configuration age limit by default |

`Tracon:Retention:Enabled` defaults to `false`. With no explicit database policy,
nothing is deleted automatically. When you enable configuration defaults, run events
default to 30 days, tool invocations to 90 days, traces to 14 days, completed jobs to
30 days, idempotency keys to one day, and run inputs to 30 days. Session and
conversation deletion still require an explicit policy.

Preview a retention rule before you execute it. If archival is enabled but no
`IArchiveSink` is registered, Tracon does not delete the rows.

Retention removes data by age; it never touches `audit_log`, which is a separate,
tamper-evident trail (`GET /api/audit/verify`) and stays outside any retention
target on purpose. If a data subject request (export or erasure by identity, not
age) is part of your compliance posture, register an `IDataSubjectResolver` — see
[Data subject rights](/concepts/governance/#data-subject-rights). Without
one, the export and erasure endpoints return `409` rather than a silent no-op.

## Production-sensitive defaults

| Boundary | Default | Production decision |
|---|---|---|
| Remote access | Off | Enable only with a registered authorization policy or scoped key strategy |
| Role-policy enforcement | Off | Turn on so a missing policy stops startup |
| Diagnostics endpoint | Off | Enable only for a protected operations path |
| Multi-tenancy | Off | Every request resolves to the single default tenant until `UseTenancy` turns it on. Decide before a second tenant's data exists |
| Session ownership | Off | `Tracon:SessionOwnership:Enabled`. **Not retroactive**: a session opened while it was off keeps a null owner forever, so turn it on before sessions accumulate rather than after |
| Request rate limiting | Off | `Tracon:RateLimit:Enabled`, 60 permits per window once on. A quota caps spend over a period; a rate limit caps a burst. They are not substitutes for one another |
| At-rest content encryption | Off | `Tracon:ContentProtection:Enabled` (AES-256-GCM). Turning it on does not encrypt rows already written, and turning it off does not decrypt them |
| Content inspection | Off | `AddTracon()` registers no guard and never adds the inspection wrapper. Even once added, the built-in guard carries no rules until you choose pattern families |
| Run event poll interval | 250 ms | Lower values reduce SSE latency but increase database reads |
| Background job worker | On, concurrency 2 per process | Separate or size workers deliberately |
| Circuit breaker | On, opens after 5 failures for 30 seconds | Align alerts and upstream retry policy |
| Singleton execution | Off | Enable with SQL when one cluster-wide periodic owner is required |
| Orphan reconciliation | Off | Enable after choosing heartbeat and orphan thresholds |
| Retention defaults | Off | Define legal, privacy, and capacity policy before data grows |
| Skill script execution | Off | Leave off unless the host is isolated and the threat model permits OS processes |
| Private network egress | Refused | Allow only if MCP servers or provider endpoints really are on the internal network |
| Agent-graph token budget | On, 200,000 tokens shared per call tree | Raise it for a tree with genuinely long tool loops, or set a `AgentGraph.MaxTotalCost` cap alongside it |
| Agent-graph time budget | Off | Set `AgentGraph.MaxDuration` where a queued run's lease renewal is otherwise the only thing keeping it going |

Six of these rows — multi-tenancy, session ownership, at-rest content encryption,
content inspection, request rate limiting and retention defaults — can be enforced
rather than remembered. `RequireProductionProfile()` turns a skipped one into a
failed startup; see [Start from a production
registration](#start-from-a-production-registration). The other rows stay a reading
exercise: their defaults are already the strict ones, or the binding gate
(`RequireCustomBinding<T>()`) already covers them.

## Upgrading: outbound targets and configuration keys

Three boundaries tightened, and all three can refuse something a running setup
accepted before. Reading is unaffected in each case; only writing, connecting, and
long-running calls change.

**Private network targets are refused on all three outbound surfaces.** Webhook
delivery already behaved this way; MCP server connections and per-tenant provider
endpoints now do too. If your MCP servers run inside the internal network, this is a
one-line change:

```json
{
  "Tracon": {
    "Egress": {
      "AllowPrivateNetworkTargets": true
    }
  }
}
```

The rejection message names that setting, so an operator who hits it can act without
reading this page. `Tracon:Webhooks:AllowPrivateNetworkTargets` still works and
still applies to webhook delivery only; either setting being on is enough for a
webhook target.

**The agent-graph token budget now actually cuts a run off.** The default
(`AgentGraph.MaxTotalTokens`, 200,000) always existed, but earlier releases only
checked it before starting a *new* child run — a single agent's own tool loop could
run past it freely. It is now enforced between model turns for every run, so a tree
whose tool loop genuinely needs more than 200,000 tokens now ends `Failed` with
`error_class: QuotaExceeded` where it previously would have finished. Raise the
setting, or set it to `0` to remove the limit:

```json
{
  "Tracon": {
    "AgentGraph": {
      "MaxTotalTokens": 500000
    }
  }
}
```

**Configuration key names must sit under an allowed prefix.** MCP server definitions
and webhook subscriptions join inbound triggers and provider bindings in this rule. A
record whose key name sits outside its prefix can still be **read** and listed, but
saving it again is refused with a message naming both the field and the prefix.

| Record | Field | Required prefix |
|---|---|---|
| MCP server | `authorizationConfigurationKey`, `oauthClientSecretConfigurationKey` | `Tracon:McpSecrets:` |
| Webhook subscription | `secretConfigurationKey` | `Tracon:WebhookSecrets:` |

Fix each record by moving the key name under the prefix and re-writing the value under
the new name in your secret store. There is no migration helper and this is
deliberate: it is one field per record, and what moves is a **name**, not a secret
value. Alternatively, widen the prefix through
`Tracon:Mcp:AllowedConfigurationPrefix` or
`Tracon:Webhooks:AllowedConfigurationPrefix` — but a prefix broad enough to cover
an arbitrary key removes the boundary it exists to provide.

## When the state preflight comes back red

Run `tracon state-check` with the **new** tool version against a copy of
production data before every upgrade — see [the upgrade
window](/reference/versioning/) for what the command reports and what its
answer is worth. Exit code `3` means it found stored state the new build
cannot read. This is what to do about it.

The command itself never changes anything, so a red result costs you nothing
but the time it took to run.

**1. Read which of the two answers came back red.** They call for different
actions:

| What the output says | What it means | What to do |
|---|---|---|
| `... NOT readable by this build` on a generation line | Rows carry a Tracon envelope generation **newer** than the build you are installing. You are downgrading, or deploying a mixed package graph | Do not deploy. Install the version that wrote those rows, or newer. This is a version selection mistake, not a data problem |
| `unreadable: session ...` on a sampled row | The Tracon envelope is fine; Microsoft Agent Framework cannot deserialize the body it wrote earlier. The message names both the recorded and the running framework version | Continue to step 2 |

**2. Decide whether those sessions have to survive the upgrade.** They often do
not — a session is a conversation, and most are minutes old. `state-check`'s
generation counts tell you how many rows are in play; your own retention policy
tells you how long they were going to live anyway.

If they do not have to survive: clear the affected sessions and checkpoints
before the upgrade, or let retention age them out and upgrade after. A run that
starts after the upgrade opens a new session and is unaffected.

**3. If they do have to survive, drain instead of cutting over.** Stop accepting
new work, let in-flight runs finish under the **old** build, and only then
deploy. Draining is a supported, tested shutdown path: the host stops taking new
jobs, waits for running ones, and does not abandon them. The sessions that
existed only for those runs are finished business by the time the new build
starts.

**4. Keep the old runtime available until you have a green preflight, and not
longer.** "Roll back the application" does not roll back the database —
migrations are forward-only, as [the migration
strategy](#choose-a-migration-strategy) above says. The old runtime is a way to
finish draining, not a permanent escape hatch, and the [supported upgrade
window](/reference/versioning/) is what bounds how long an old version stays
readable at all. Plan the drain window, not an indefinite dual-runtime setup.

**5. If none of the above fits, treat it as a compatibility defect and report
it** with the facts from [Record the version in incident
reports](/reference/versioning/): the exact package versions, the recorded and
running Microsoft Agent Framework versions from the failure line, and the
generation counts. Do not attach the state payload itself — it is conversation
content.

:::caution[What a clean preflight does and does not promise]
The generation count covers every row. The decode covers `--sample` rows of
each generation, five by default. A clean run says the rows that were read came
back readable; it does not say every row would. Raising `--sample` buys more
evidence at the cost of time, and no value of it turns the sample into a
survey.
:::

## What a failure actually does

The behaviours below are measured, not inferred. Each one has a failure manifest
in the test suite that runs on every build, and each is stated as what was
observed rather than as a guarantee.

:::caution[Measured, not promised]
These observations describe how one Tracon version behaves against one SQL
store. They are not a statement that Tracon supports multiple nodes, and
they set no service level. SQLite remains a single-process store; run the
scenarios below on PostgreSQL or SQL Server.
:::

### A worker process dies mid-job

The job stays leased to the dead worker until its lease expires. No other worker
touches it before then — the lease is a database row, so a process that dies
without releasing anything still holds it for the remainder of the term. Once
`LeaseDuration` elapses, another worker leases the job, `Attempt` increments, and
the job runs again from the start.

Two consequences follow, and both are yours to design for:

- **Recovery is not instant.** A job's worst-case stall after a crash is one full
  `LeaseDuration`. That value is the trade: shorter recovers faster, longer
  tolerates more pause, garbage collection, and network wobble before a live
  worker's own job is stolen from underneath it.
- **Execution is at-least-once, never exactly-once.** The crashed attempt's
  side effects already happened. Item progress is idempotent — a re-reported item
  does not double the job's counters — but nothing outside the database is.
  Application-level idempotency for irreversible actions is not optional.

What does **not** happen is two workers inside the same job at once. That is the
guarantee the lease actually provides.

### The database is unreachable

Three different behaviours, deliberately:

- **Reads and queue operations fail loudly.** A store call raises the provider's
  exception. It never degrades into an empty list, because an empty answer is
  indistinguishable from "no data" and would be read as success.
- **Run recording fails quietly.** The recorder disables itself for that run,
  logs, and the run continues. Observability must not break functionality.
- **Worker processes stay up.** A failed queue tick is logged and the next tick
  runs, and the process resumes when the database returns. Work in flight when
  the database went away is lost the same way a crash loses it — the job is not
  completed, and it becomes leasable again once its lease expires.

### The provider times out

A timeout moves to the next link of the model fallback chain, and the client sees
an ordinary answer. Without a fallback the run is recorded as `Failed` with the
`Timeout` error class and the endpoint answers `502`.

:::note[Fixed in a recent version]
Earlier versions recorded a provider timeout as a **cancelled** run and answered
`200` with an empty body, because .NET reports an `HttpClient` timeout as a
`TaskCanceledException`. If you have dashboards or alerts keyed on cancelled runs,
expect timeouts to move out of that bucket and into `Timeout` after upgrading.
:::

### A run event sink is slow

Sink dispatch is **inline** on the recording path. A failing sink is isolated —
it is disabled for that run after its first failure and the run finishes normally
— but a *slow* sink is not: its latency is added to every event the run produces.
"Observability must not break functionality" means it cannot break a run; it does
not mean it cannot slow one. A sink that talks to a network target must buffer
internally and return immediately.

### A retention pass deletes at volume

A delete of the rows a retention pass targets takes row locks, not a table lock.
Reads of the same table, and writes of rows the delete does not match, both run
to completion while one is in flight — verified against PostgreSQL with the
delete deliberately left uncommitted. Batch size is your lock-duration control:
a pass deletes exactly its batch and leaves the rest for the next call, which is
why cleanup never becomes one unbounded statement.

### Many subscribers read one run's stream

Every subscriber reads the same complete sequence from the store, from the
beginning — a late subscriber has not missed anything, and an abandoned reader
changes nothing for the others. The cost model to size for is therefore
*subscribers x events* of database reads, not one read shared between them.
(What is measured is the recorded stream of a finished run, which is the case
where every subscriber must agree exactly; a live run's tail adds polling on top
of the same per-subscriber reads.)

### Two versions are up during a rolling upgrade

Migrations are additive, so a process running the older version keeps reading and
writing the tables it knows while the newer schema is already applied, and both
lease from the same queue without collision. Verify the window before you open it
with `tracon state-check`, which reads and writes nothing. Keep the window
short and planned; an indefinite dual-version deployment is not a supported shape.

Unlike the other behaviours on this page, this one is measured with **one** build
standing in for both sides — a process that enabled fewer optional migration sets
than the schema has. Two released Tracon versions running side by side is not
something we have measured, which is another reason to keep the window short.

## What one environment measured

The numbers below come from measured runs, not from a model. They describe
**one machine, one PostgreSQL version and one configuration**, and they are
**not an SLA and not a guaranteed capacity**. Read them as a shape — how the
three request paths differ from each other, and what each one writes — rather
than as a number your deployment will reproduce.

Measured on Darwin 25.6.0 (Apple Silicon), 10 logical processors, 16 GiB,
.NET 10.0.100, PostgreSQL 18.4 in the `pgvector/pgvector:pg18` image. The host
consumed Tracon by `PackageReference` at one exact version from an isolated
feed; the driver was a separate process calling it over loopback TCP.

The load sweep and the arrival runs measured Tracon packed from commit
`e44d89f5`; the worker and soak runs measured `df45a7ba`. Only the measurement
apparatus differs between those two commits — the shipped source is byte
identical (`git diff e44d89f5..df45a7ba -- src/` is empty). Each run's own
manifest, under `bench/capacity/measurements/`, names the commit it measured.

**The model is a synthetic fixture that makes no network call**: one second of
total think time, a tool call, and a 5 KB answer in twenty chunks. Substituting
a real provider changes every latency number on this page and most of the
throughput ones.

Each row below merges the samples of three 60-second repeats and takes the
percentile once, after a 15-second warm-up, against an empty database.

| Path | Concurrency | n | p50 | p95 | Completed/s |
|---|---|---|---|---|---|
| Buffered (`Idempotency-Key`) | 1 | 174 | 1044 ms | 1057 ms | 0.94 <!-- capacity: kind=latency profile=sweep seed=empty scenario=buffered concurrency=1 indicative=p95 --> |
| Buffered | 8 | 1 384 | 1048 ms | 1084 ms | 7.47 <!-- capacity: kind=latency profile=sweep seed=empty scenario=buffered concurrency=8 --> |
| Buffered | 32 | 5 472 | 1060 ms | 1108 ms | 29.5 <!-- capacity: kind=latency profile=sweep seed=empty scenario=buffered concurrency=32 --> |
| Buffered | 64 | 10 944 | 1055 ms | 1120 ms | 59.1 <!-- capacity: kind=latency profile=sweep seed=empty scenario=buffered concurrency=64 --> |
| Streaming (SSE) | 1 | 153 | 1185 ms | 1226 ms | 0.83 <!-- capacity: kind=latency profile=sweep seed=empty scenario=streaming concurrency=1 indicative=p95 --> |
| Streaming | 8 | 1 192 | 1215 ms | 1277 ms | 6.44 <!-- capacity: kind=latency profile=sweep seed=empty scenario=streaming concurrency=8 --> |
| Streaming | 32 | 5 226 | 1104 ms | 1189 ms | 28.1 <!-- capacity: kind=latency profile=sweep seed=empty scenario=streaming concurrency=32 --> |
| Streaming | 64 | 10 718 | 1074 ms | 1136 ms | 57.4 <!-- capacity: kind=latency profile=sweep seed=empty scenario=streaming concurrency=64 --> |
| Queued (`Prefer: respond-async`) | 1 | 140 | 1309 ms | 1330 ms | 0.75 <!-- capacity: kind=latency profile=sweep seed=empty scenario=queued concurrency=1 indicative=p95 --> |
| Queued | 8 | 1 104 | 1318 ms | 1336 ms | 5.93 <!-- capacity: kind=latency profile=sweep seed=empty scenario=queued concurrency=8 --> |
| Queued | 32 | 1 392 | 4457 ms | 4494 ms | 6.75 <!-- capacity: kind=latency profile=sweep seed=empty scenario=queued concurrency=32 --> |
| Queued | 64 | 1 488 | 8773 ms | 8946 ms | 6.31 <!-- capacity: kind=latency profile=sweep seed=empty scenario=queued concurrency=64 --> |

At concurrency 1 each repeat contributed only about 50 samples, so those three
p95 values rest on a merged count that no single window supported. Treat the
concurrency-1 rows as indicative.

Median latency against the 10,000-run database matched the empty one to within
about 2%<!-- capacity: kind=value profile=sweep field=p50-spread --> at every step, so those rows are not repeated here. **The tails
did not match**: buffered at concurrency 1 reached a p99 of
1721 ms<!-- capacity: kind=value profile=sweep scenario=buffered seed=full concurrency=1 field=latency.p99 --> against the full database versus
1073 ms<!-- capacity: kind=value profile=sweep scenario=buffered seed=empty concurrency=1 field=latency.p99 --> against the empty one, and queued at
concurrency 64 reached 10429 ms<!-- capacity: kind=value profile=sweep scenario=queued seed=full concurrency=64 field=latency.p99 --> versus
9003 ms<!-- capacity: kind=value profile=sweep scenario=queued seed=empty concurrency=64 field=latency.p99 -->. Existing data moved the outliers, not the
median.

**The queued path has a ceiling and the synchronous paths did not reach one.**
Buffered and streaming scale nearly linearly to 64 concurrent requests. Queued
stops at about 6 to 7 runs per second: between concurrency 8 and 32 its
throughput moved 5.93/s to 6.75/s while p50 latency grew from 1318 ms to
4457 ms. The extra load bought queue depth, not work. **Nothing was refused** —
a client that waits is never told it is queueing, so this ceiling is invisible
to an error rate and visible only in latency. It is `MaxConcurrentJobs` times
the work per job; raise the first or shorten the second.

Under open-loop load — a send plan that does not wait for the previous answer —
the same limit appears as backlog rather than as slowness. Totals are across
three repeats:

| Planned/s | Sent | Never sent | Completed | Queue wait p95 |
|---|---|---|---|---|
| 1 | 180 of 180 | 0 | 180 | 0.05–0.11 s <!-- capacity: kind=arrival profile=arrival rate=1 --> |
| 4 | 720 of 720 | 0 | 720 | 0.08–0.10 s <!-- capacity: kind=arrival profile=arrival rate=4 --> |
| 8 | 1 440 of 1 440 | 0 | 1 440 | 5.69–5.70 s <!-- capacity: kind=arrival profile=arrival rate=8 --> |
| 16 | 1 672 of 2 880 | 1 208 | 1 668 | 16.5–16.7 s <!-- capacity: kind=arrival profile=arrival rate=16 --> |

At 16 requests per second the driver's own in-flight cap bound before the
server's did, so the "never sent" column is a limit of the measurement, not of
Tracon. It is shown rather than hidden because a driver that quietly waits for
a free slot reports a load nobody offered.

### What a run writes

Per completed run, measured as the growth of an isolated database across the
window, after the drain, with index and TOAST separated from the table body.
Rows and bytes are filtered differently on purpose: byte growth also counts
bloat, so a window that autovacuum ran inside cannot contribute one, while a
row count is immune to it and stays.

| Path | Rows per run (3 repeats) | Bytes per run | Largest contributor |
|---|---|---|---|
| Buffered | 10.5 | ~8.6 KiB (1 clean repeat) | `run_events` (6 rows) <!-- capacity: kind=storage profile=sweep seed=empty concurrency=1 scenario=buffered --> |
| Queued | 10.9 | ~10.3 KiB (1 clean repeat) | `run_events` (6 rows), plus one `jobs` row <!-- capacity: kind=storage profile=sweep seed=empty concurrency=1 scenario=queued --> |
| Streaming | 27.8 | ~14.0 KiB (1 clean repeat) | `run_events` (24 rows) <!-- capacity: kind=storage profile=sweep seed=empty concurrency=1 scenario=streaming --> |

All three rows describe the same cell — one concurrent request against an empty
database — so they can be read against each other. Each byte figure comes from
the single window that autovacuum stayed out of, and is marked as such rather
than presented as an average; the row counts come from all three windows.

**Streaming writes about 2.6 times the rows of a buffered run**, because every
message delta is recorded. That is the single largest lever on storage growth
here, and it is a recording choice rather than a transport cost. A linear
projection of the streaming figure to a million runs is roughly 14 GB before
retention — a projection, not a measurement, and only meaningful inside the
range above. Retention was off; with it on these numbers answer a different
question.

### Adding worker processes

The queued path was also measured against 1, 2 and 4 separate worker processes
leasing the same queue, with the offered load held constant. The work did
spread — across three repeats four workers took 33%, 31%, 19% and 17% of the
jobs — and **no job ever ran in two workers at once**, in any repeat, at any
worker count. Every job ran exactly once: attempts per accepted job was 1.00
throughout.

Total throughput did not change (5.93, 5.93 and 5.96 runs/s for one, two and
four workers), because at that offered load one worker already kept up. Adding
workers moved the work; it did not create more of it.

**Important: this says nothing about running Tracon on multiple machines.** It
is one machine, one database and one clock. Network partition, clock skew and
inter-machine latency were not measured and are not implied.

### Thirty minutes at a steady load

An equal mix of the three paths at concurrency 8 — the highest step the queued
path finishes without queueing — ran for thirty minutes: **12 515 runs accepted,
12 515 completed**, none missing from the store, no content mismatch, no gap in
any recorded event stream, and nothing recorded under the wrong tenant. A read
of one tenant's run carrying the other tenant's header was refused. The drain
after the window took 0.2 seconds.

Host memory did not accumulate. It peaked at 379 MiB in the first minutes
(warm-up and JIT), then settled; the final five minutes averaged 162 MiB, lower
than minutes 10–15. Steady-state p99 was 1081 ms buffered, 1251 ms streaming and
1340 ms queued, with the three paths sharing 6.94 runs/s between them.

These figures are tied to the versions and commits named above. They are a
shipped claim, so they are not carried forward to a later release without
re-measuring. The manifests, summaries, reports and charts for all four runs are
kept in the repository under `bench/capacity/measurements/`, and
`bench/capacity/README.md` describes how to reproduce them.

## Release and capacity caveats

Tracon and its Microsoft Agent Framework hosting dependencies are pre-release.
Pin an exact NuGet version, run contract and integration tests before upgrades, and
review generated API changes as part of the release. `Tracon.AspNetCore` is not
Native AOT compatible.

Tracon does not provide an operating-system sandbox for skill scripts. If you
enable them, run the service as an unprivileged identity in an isolated container,
restrict its filesystem and network, and treat every script as deployed code.

Capacity planning must include model concurrency, SQL event volume, job polling,
message-delta recording, trace cardinality, attachment storage, and provider rate
limits. A healthy HTTP process can still overload a provider if worker count is
scaled without a matching quota.

## Deployment checklist

- [ ] Pin one tested Tracon version and one SQL provider.
- [ ] Load database and provider credentials only from a secret facility.
- [ ] Run or verify migrations before readiness can pass.
- [ ] Require a real authentication policy and all three role policies.
- [ ] Declare every embedding point the deployment depends on with `RequireCustomBinding<T>()`.
- [ ] Answer every row of [Production-sensitive defaults](#production-sensitive-defaults) on
      purpose. Multi-tenancy, session ownership, rate limiting, at-rest encryption, and content
      inspection are each off until you turn them on, and session ownership is not retroactive.
- [ ] Call `RequireProductionProfile()` so those six decisions cannot be skipped in
      silence, and accept by name the ones this deployment carries deliberately.
- [ ] Configure trusted proxy headers and TLS without relying on loopback identity.
- [ ] Choose combined or split workers and calculate cluster-wide concurrency.
- [ ] Enable singleton execution and orphan reconciliation only with shared SQL state.
- [ ] Test direct and queued cancellation through the actual load balancer.
- [ ] Size `LeaseDuration` deliberately: it is the worst-case stall after a worker crash.
- [ ] Make every irreversible side effect idempotent; job execution is at-least-once.
- [ ] Export the `Tracon` activity source and meter; alert on readiness and job age.
- [ ] Define retention, privacy, backup, and restore procedures for every stored data class.
- [ ] Run `tracon state-check` with the new tool version against a copy of
      production data before every upgrade, and know what a `3` means (above).
- [ ] Register `IDataSubjectResolver` if data subject export/erasure requests are part of your compliance posture.
- [ ] Keep skill scripts and diagnostics disabled unless their operational need is explicit.
- [ ] Decide private network egress deliberately, and move every stored configuration
      key name under its allowed prefix before upgrading.

:::caution[Production caveat]
A SQL database makes records durable; it does not make external tool side effects
exactly once. A worker can fail after a side effect and before its lease is committed.
Use application-level idempotency for payments, messages, writes, and every other
irreversible action. Test the crash boundary, not only the successful path.
:::

## Troubleshooting

| Symptom | Check |
|---|---|
| The application fails during startup | Read the first migration or options-validation error; verify connection-string resolution, database permissions, schema rules, and migration checksums |
| Readiness is `Unhealthy` | Check SQL reachability and pending migrations before provider status |
| Readiness is `Degraded` after startup | Refresh model health, inspect open circuits, and verify that only one SQL provider is registered |
| Remote users get `403` | Confirm `AllowRemoteAccess`, the base policy, role policy, API-key scope, tenant, and trusted proxy configuration |
| Startup reports missing role policies | Register all `Tracon.Reader`, `Tracon.Operator`, and `Tracon.Admin` policies or keep strict remote mapping disabled until they exist |
| A direct run cannot be canceled | Route the request to the owning process or install a distributed `IRunCancellationRegistry`; a restarted owner no longer has the in-process registration |
| Jobs do not move | Confirm a worker is enabled, shares the same SQL database, and passed the schema-ready gate |
| A job restarted by itself | A worker holding its lease died or stalled past `LeaseDuration`; check `Attempt` and the worker's own liveness before suspecting the handler |
| Runs are slow but the model is not | Check registered `IRunEventSink` implementations; sink dispatch is inline and its latency is paid per event |
| Database volume grows without bound | Retention is off by default; create and preview policies, then verify cleanup history and archival behavior |
| Cost dashboards are empty | Confirm the provider reported token usage and that the exact model has a price; unknown cost is not zero |

## Read next

- [Securing the endpoints](/getting-started/security/) — the authentication and authorization decisions this topology assumes
- [Persistence](/getting-started/persistence/) — choosing and migrating the store the topology writes to
- [Observability and cost](/guides/observability/) — what to watch once it is running
