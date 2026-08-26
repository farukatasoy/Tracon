# AgentPrism.Sqlite

SQLite persistence layer for [AgentPrism](https://agentprism.doayen.web.tr).

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

## Sharing a data source

Give `DataSource` instead of `ConnectionString` when your host already owns a
`DbDataSource` for the same database file and you want AgentPrism using that same
instance:

```csharp
options.DataSource = yourDbDataSource;   // ConnectionString is then not required
```

AgentPrism never disposes an instance it did not build; the caller keeps
ownership. `Microsoft.Data.Sqlite` does not itself offer a `DbDataSource`
implementation, so this is for a `DbDataSource` adapter you write yourself — see
`AgentPrism.PostgreSql`'s `https://agentprism.doayen.web.tr/guides/ef-core/` for
the pattern.

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

## Read contract view

Set `EnableReadViews = true` to publish `{prefix}runs_v1` (no separating dot — SQLite
has no schema concept), a narrow, versioned, read-only view over run data — query it
with your own SQL or map it as an EF Core keyless entity, without depending on the
internal `runs` table shape. Off by default; a deployment that never turns it on
never sees the object. See
[Read contract views](https://agentprism.doayen.web.tr/reference/read-views/) for
the column list and the compatibility rule.

## AOT

This package's AOT status is **not measured**. `SQLitePCLRaw` carries a native library; this
typically affects publish behavior. Use `AgentPrism.PostgreSql` for setups that require AOT.

## Two providers at once

If `UsePostgreSql()`, `UseSqlServer()`, and `UseSqlite()` are called in the same chain, **the
last registration wins** and a warning is logged at startup. This is a configuration error;
call only one.

## Links

- Guide: <https://agentprism.doayen.web.tr/getting-started/persistence/>
- Capability map: <https://agentprism.doayen.web.tr/capabilities/>
- API reference: <https://agentprism.doayen.web.tr/api/>
