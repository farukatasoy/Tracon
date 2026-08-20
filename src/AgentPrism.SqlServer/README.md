# AgentPrism.SqlServer

SQL Server persistence layer for [AgentPrism](https://github.com/farukatasoy/AgentPrism).

Agent definitions, sessions, conversations, runs, events, workflows, job queue,
evaluation, quota, and webhook records are stored in a separate `agentprism`
schema. The consumer's `dbo` schema is left untouched.

## Setup

```bash
dotnet add package AgentPrism.SqlServer
```

```csharp
builder.AddAgentPrism()
       .UseSqlServer(builder.Configuration.GetConnectionString("AgentPrism")!);
```

With options:

```csharp
builder.AddAgentPrism()
       .UseSqlServer(options =>
       {
           options.ConnectionString = "...";   // secret: user-secrets or an environment variable
           options.SchemaName = "agentprism";
           options.CommandTimeoutSeconds = 30;
           options.AutoApplyMigrations = true;
       });
```

> **The connection string is a secret and is not written to a file.** Use
> `dotnet user-secrets`, an environment variable, or a secret manager.

## Supported versions

| Environment | Status |
|-------|-------|
| SQL Server 2019+ | Supported, tested in CI (2022 image) |
| Azure SQL Database | Supported, **not tested in CI** — cannot be spun up via container |
| SQL Server 2017 and earlier | Not supported |

## Migrations

Embedded `.sql` files are applied automatically at application startup. They are
protected with `sp_getapplock`: if multiple replicas start at the same time, only
one applies them.

In production you can disable auto-apply and run `MigrationRunner` as a separate
deployment step:

```csharp
options.AutoApplyMigrations = false;
```

Migration numbers are **per provider**; they do not align with
`AgentPrism.PostgreSql` and are not meant to.

## AOT

This package is **not AOT-compatible**: `Microsoft.Data.SqlClient` is not marked
for trimming. For setups that need AOT, use `AgentPrism.PostgreSql`.

## Two providers at once

If `UsePostgreSql()` and `UseSqlServer()` are called in the same chain, **the last
registration wins** and a warning is logged at startup. This is a configuration
error; call only one of them.

## Links

- Guide: <https://farukatasoy.github.io/AgentPrism/getting-started/persistence/>
- Capability map: <https://farukatasoy.github.io/AgentPrism/capabilities/>
- API reference: <https://farukatasoy.github.io/AgentPrism/api/>
