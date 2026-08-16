# AgentPrism.Sqlite

SQLite persistence layer for [AgentPrism](https://github.com/farukatasoy/AgentPrism).

For single-file setups: demos, embedded/edge scenarios, and testing with real SQL behavior
beyond in-memory stores. Tables carry a configurable **prefix** (default `agentprism_`) so
they do not collide with the consumer's own tables; SQLite has no schema concept.

## Setup

```bash
dotnet add package AgentPrism.Sqlite
```

```csharp
builder.AddAgentPrism()
       .UseSqlite("Data Source=agentprism.db");
```

In memory, testing only:

```csharp
builder.AddAgentPrism()
       .UseSqlite("Data Source=:memory:");   // LIMIT: data is lost when the connection closes
```

With settings:

```csharp
builder.AddAgentPrism()
       .UseSqlite(options =>
       {
           options.ConnectionString = "Data Source=agentprism.db";
           options.TablePrefix = "agentprism_";
           options.CommandTimeoutSeconds = 30;
           options.AutoApplyMigrations = true;
       });
```

## SQLite's real limits

These are not hidden; they are reported in this README and in the `/api/meta` output.

| Limit | Consequence |
|-------|-------------|
| **Single writer** | Concurrent writes serialize. At high run volume, `run_events` writes become a bottleneck |
| WAL required | Set automatically on connection open; cannot be disabled |
| `busy_timeout` | Set to 5000 ms |
| Weak type system | No `datetimeoffset`, no `uuid`, no `decimal` — all encoded as text/number |
| No network | Single-process; **cannot be used in a multi-instance deployment** |
| No `SKIP LOCKED` | The job queue (`RunWorker`) runs with a single worker; cannot be multi-instance |

> These limits do not make SQLite bad; **using it in the wrong place** does.

## Migrations

Embedded `.sql` files are applied automatically at application startup. The lock carries no
`sp_getapplock`/`pg_advisory_lock` equivalent — it is protected by a sidecar file lock
(`<database-file>.agentprism-migration-lock`). The lock is skipped for `:memory:` databases
(another process cannot share the same connection anyway).

```csharp
options.AutoApplyMigrations = false;
```

Migration numbers are **per provider**; they do not, and need not, line up with
`AgentPrism.PostgreSql` or `AgentPrism.SqlServer`.

## AOT

This package's AOT status is **not measured**. `SQLitePCLRaw` carries a native library; this
typically affects publish behavior. Use `AgentPrism.PostgreSql` for setups that require AOT.

## Two providers at once

If `UsePostgreSql()`, `UseSqlServer()`, and `UseSqlite()` are called in the same chain, **the
last registration wins** and a warning is logged at startup. This is a configuration error;
call only one.
