---
title: Persistence
description: Choose PostgreSQL, SQL Server, or SQLite and operate AgentPrism migrations safely from development to production.
sidebar:
  order: 4
---

Without a database every store is in memory and everything ends with the process.
That is deliberate — it makes the first agent work with no infrastructure — but it is
not where you stop.

:::caution[In production, AgentPrism says so out loud]
Start a host in the `Production` environment while storage is still in memory and
AgentPrism writes one warning at startup, naming the stores that do not survive a
restart. In-memory storage stays a supported mode — the warning never fails
startup and there is no switch to silence it, because a production installation
losing its runs on the next deployment should not be a quiet fact. The same
judgement is what `GET /api/meta` reports as `storage.persistent`, and what the
console's settings screen shows.
:::

## Pick one

```csharp
builder.AddAgentPrism()
       .UsePostgreSql(connectionString);   // AgentPrism.PostgreSql
```

| Package | Choose it when |
|---|---|
| `AgentPrism.PostgreSql` | The default. The only one with vector search for knowledge |
| `AgentPrism.SqlServer` | You already run SQL Server |
| `AgentPrism.Sqlite` | One node, or a durable local development setup |

All three implement the same store contracts and pass the same shared contract tests.
Their operational limits differ: only PostgreSQL supports Knowledge, only PostgreSQL
keeps the AOT promise, and SQLite is a single-node choice.

:::caution[The connection string is a secret]
It never belongs in `appsettings.json`. Use `dotnet user-secrets` in development and
the environment or a secret store in production. The same rule runs through the whole
product: a webhook or MCP registration stores the *name* of the configuration key its
secret is read from, never the value.
:::

Binding from configuration is the usual shape:

```csharp
.UsePostgreSql(builder.Configuration.GetSection(AgentPrismPostgreSqlOptions.SectionName))
```

```json title="appsettings.json"
{
  "AgentPrism": {
    "PostgreSql": {
      "ConnectionString": "",
      "SchemaName": "agentprism",
      "AutoApplyMigrations": true,
      "CommandTimeoutSeconds": 30,
      "EnableKnowledge": false,
      "EnableReadViews": false
    }
  }
}
```

## How each provider isolates its tables

| Provider | Namespace | Migration coordination |
|---|---|---|
| PostgreSQL | Separate `agentprism` schema by default | `pg_advisory_lock`, scoped to the schema |
| SQL Server | Separate `agentprism` schema by default; your `dbo` objects stay untouched | `sp_getapplock`, scoped to the schema |
| SQLite | No schema support; `agentprism_` table prefix by default | A sidecar file lock next to the database |

Rename `SchemaName` or `TablePrefix` when your conventions require it. A bare SQLite
`Data Source=:memory:` connection is rejected because each opened connection would
see a different database; use a shared in-memory URI for tests.

:::note[Knowledge is opt-in and needs pgvector]
The migration set that creates the `vector` extension and the `document_embeddings`
table only applies when `EnableKnowledge` is `true` — off by default, so a managed
PostgreSQL instance without permission to install extensions works with no
configuration at all. Turn it on only if an agent uses Knowledge:

```csharp
.UsePostgreSql(options =>
{
    options.ConnectionString = connectionString;
    options.EnableKnowledge = true;
})
```

With it off, an agent definition that sets `Memory.EnableVectorSearch` fails
compilation with a clear error instead of a database error at run time. Embedding
`Dimensions` become part of the column type: changing embedding models later needs a
schema migration and a re-embed of existing documents. See
[Knowledge](/guides/knowledge/).
:::

:::note[Read views are opt-in on all three providers]
`EnableReadViews` publishes `runs_v1`, a versioned, read-only SQL view over run
data — off by default, so a deployment that never turns it on never sees the
object. Query it with your own SQL or map it as an EF Core keyless entity. See
[Read contract views](/reference/read-views/).
:::

## Migrations run at startup

The SQL files ship embedded in the assembly and are applied when the application
starts, unless that responsibility is moved to its own deployment step:

```mermaid
flowchart TD
    accTitle: Two ways a migration is applied
    accDescr: With AutoApplyMigrations true, the application applies pending migrations itself at startup. With it false, a separate agentprism migrate step applies the schema first, and the application only verifies it before starting.
    START["Application starts"] --> CHECK{"AutoApplyMigrations"}
    CHECK -->|"true (default)"| APPLY["Applies pending migrations itself<br/>provider lock serializes concurrent instances"]
    CHECK -->|"false"| SEPARATE["agentprism migrate<br/>runs as its own deployment step, no app needed"]
    SEPARATE --> APP2["Application starts, verifies the schema, does not write"]
    APPLY --> READY["Ready"]
    APP2 --> READY
```

Two properties make in-process application safe with several instances starting at
once:

- The runner takes the provider-specific lock shown above, so instances serialize
  instead of racing.
- Each applied file's SHA-256 is recorded. If the content later differs from what was
  applied, startup **fails loudly** rather than running against a schema that is not
  what the code expects.

:::danger[Do not edit an applied migration]
The checksum covers the file's whole text, comments included. Editing a file that has
already been applied makes every existing database refuse to start. Add a new
migration instead. If you must take such a change, drop and recreate the schema in
that environment first.
:::

:::caution[A migration can be one-way]
Most migrations only add. Some rewrite or drop a column, and while AgentPrism is in
preview a release may contain one: the data is carried across by the migration
itself, but there is no downgrade path back to the older schema. Take a backup
before upgrading a database you cannot lose, and roll a version back by restoring
that backup rather than by pointing an older build at the newer schema — the
checksum check will refuse it anyway.
:::

Set `AutoApplyMigrations = false` when schema changes are their own deployment step.
AgentPrism then verifies but does not write. The diagnostics endpoint can report
whether the schema is current, but it is deliberately not mapped by default because
it exposes setup details:

```csharp
app.MapAgentPrism("/agentprism", options =>
{
    options.EnableDiagnosticsEndpoint = true;
});
```

After that opt-in, `GET /agentprism/api/diagnostics` is an Admin surface and still
passes through the configured access layers.

Something still needs to **apply** the schema before the application starts with
`AutoApplyMigrations = false`. The `agentprism` CLI does that as its own step,
against the database directly — no running application required:

```bash
agentprism migrate --provider postgres --connection "$AGENTPRISM_CONNECTION"
```

Running it again applies nothing (`0 applied`) and exits `0`; `agentprism migrate
status` lists pending migration names without writing. See the [typed client and
CLI guide](/guides/cli/) for setup and the rest of the commands.

## Giving your own data source instead of a connection string

Each provider's `Options.DataSource` field accepts a `DbDataSource` you built
yourself instead of `ConnectionString` — most useful when your host already owns
one, for example an EF Core `DbContext` configured with an `NpgsqlDataSource`:

```csharp
.UsePostgreSql(options => options.DataSource = yourDataSource)
```

AgentPrism never disposes an instance it did not build; ownership stays with
whoever created it. Building two separate data sources from the identical
connection string does **not** share a connection pool — see
[Two data planes, one connection pool or two](/guides/embedding/#two-data-planes-one-connection-pool-or-two)
and, for the full EF Core pattern,
[Two connection planes: EF Core and AgentPrism](/guides/ef-core/).

## What changes once it is durable

Runs, events, and tool calls survive restarts, so the console shows real history
rather than the current process. Sessions can be read back as chat history rather
than an opaque blob — which is also what makes branching a conversation possible.
Queued runs, schedules, evals, experiments, quotas, and the pending-approval mailbox
all become usable, since they depend on state outliving a request. A resolved
[approval presentation](/concepts/governance/#approvals) is persisted next to the
raw call arguments, so it survives a restart the same way the arguments do.

It is also what makes recovering from a crash possible at all: a run's recorded
tool calls are what an [automatically continued run](/guides/reliability/#continue-an-interrupted-run-automatically)
replays instead of repeating, and orphan reconciliation itself only has a stale
`Running` row to find because that row, and every tool call it already made, outlived
the process that opened it.

Session and workflow checkpoint rows carry a version stamp of their own, separate
from the schema migrations above: AgentPrism's envelope around the row, and the
Microsoft Agent Framework version that wrote the opaque state inside it. See
[Versions and upgrades](/reference/versioning/#persisted-session-and-checkpoint-state)
for what that stamp promises and what happens when an old row can no longer be read.

## Keeping it from growing forever

A recorded run is data, and recorded runs accumulate. Retention policies set an age
or row limit per target — run events, tool calls, traces, jobs, webhook deliveries,
eval results, checkpoints, and more.

Database policies take precedence. When no database policy exists and
`AgentPrism:Retention:Enabled` is true, configuration falls back to built-in target
defaults, such as 30 days for run events and 14 days for spans. With retention
disabled, nothing is deleted. An `archive: true` policy also deletes nothing when no
`IArchiveSink` is registered; data loss is the failure mode the worker avoids.

Cleanup runs through the job queue. Preview a policy before you execute it:

```bash
curl 'http://localhost:5081/agentprism/api/retention/preview'
curl -X POST 'http://localhost:5081/agentprism/api/retention/run'
```

## Durability also enables governance

A durable `audit_log` can be **verified**: `GET /api/audit/verify` walks a
hash chain and reports whether any entry was altered or deleted after it was
written. And because sessions, runs, and conversations are real rows now, a data
subject's content can be found and erased by identity, not just aged out — see
[Data subject rights](/concepts/governance/#data-subject-rights).

Both endpoints accept an optional `tenantId` filter; leaving it out never means
"every tenant" — it resolves to the caller's own ambient tenant. A custom
`IAuditLog` implementation must apply this same fallback (see
[Write your own store](/guides/write-your-own-store/)).

Durable rows are also what
[content protection](/getting-started/security/#at-rest-content-protection) encrypts.

## Read next

- [Securing the endpoints](/getting-started/security/) — required reading
before this leaves your machine.
- [Write your own store](/guides/write-your-own-store/) — implement `IRunStore`
  (or another store interface) against a persistence engine none of the three
  built-in providers cover.
