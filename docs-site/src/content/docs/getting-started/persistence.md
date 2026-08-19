---
title: Persistence
description: Choose PostgreSQL, SQL Server, or SQLite and operate AgentPrism migrations safely from development to production.
sidebar:
  order: 4
---

Without a database every store is in memory and everything ends with the process.
That is deliberate — it makes the first agent work with no infrastructure — but it is
not where you stop.

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
      "EnableKnowledge": false
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
[Knowledge](/AgentPrism/guides/knowledge/).
:::

## Migrations run at startup

The SQL files ship embedded in the assembly and are applied when the application
starts. Two properties make that safe with several instances starting at once:

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

## What changes once it is durable

Runs, events, and tool calls survive restarts, so the console shows real history
rather than the current process. Sessions can be read back as chat history rather
than an opaque blob — which is also what makes branching a conversation possible.
Queued runs, schedules, evals, experiments, and quotas all become usable, since they
depend on state outliving a request.

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
[Data subject rights](/AgentPrism/concepts/governance/#data-subject-rights).

## Next

[Securing the endpoints](/AgentPrism/getting-started/security/) — required reading
before this leaves your machine.
