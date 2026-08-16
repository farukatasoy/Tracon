---
title: Persistence
description: Choosing a database, what migrations do at startup, and what changes when you add one.
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

All three implement the same contracts and are verified against the same shared
contract test suite, so the choice does not change any code you write.

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
      "CommandTimeoutSeconds": 30
    }
  }
}
```

## It stays out of your schema

Everything lives in its own schema — `agentprism` by default. Your `public` schema is
never touched and no table name can collide with one of yours. Rename it with
`SchemaName` if your conventions require it.

## Migrations run at startup

The SQL files ship embedded in the assembly and are applied when the application
starts. Two properties make that safe with several instances starting at once:

- The run holds an **advisory lock** keyed on the schema name, so instances serialize
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
AgentPrism then verifies but does not write, and `GET /api/diagnostics` reports
whether the schema is up to date and what is pending.

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

Nothing is deleted until you configure it: a target with no policy is kept forever.
And no endpoint deletes synchronously — preview first, then run, and the cleanup goes
through the job queue.

```bash
curl 'http://localhost:5081/agentprism/api/retention/preview'
curl -X POST 'http://localhost:5081/agentprism/api/retention/run'
```

## Next

[Securing the endpoints](/AgentPrism/getting-started/security/) — required reading
before this leaves your machine.
