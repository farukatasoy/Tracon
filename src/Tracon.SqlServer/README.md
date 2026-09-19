# Tracon.SqlServer

SQL Server persistence layer for [Tracon](https://tracon.dev).

Agent definitions, sessions, conversations, runs, events, workflows, job queue,
evaluation, quota, and webhook records are stored in a separate `tracon`
schema. The consumer's `dbo` schema is left untouched.

## Setup

```bash
dotnet add package Tracon.SqlServer --prerelease
```

```csharp
builder.AddTracon()
       .UseSqlServer(builder.Configuration.GetConnectionString("Tracon")!);
```

With options:

```csharp
builder.AddTracon()
       .UseSqlServer(options =>
       {
           options.ConnectionString = "...";   // secret: user-secrets or an environment variable
           options.SchemaName = "tracon";
           options.CommandTimeoutSeconds = 30;
           options.AutoApplyMigrations = true;
       });
```

> **The connection string is a secret and is not written to a file.** Use
> `dotnet user-secrets`, an environment variable, or a secret manager.

## Sharing a connection pool

Give `DataSource` instead of `ConnectionString` when your host already owns a
`DbDataSource` for the same database and you want Tracon's traffic on that
same pool:

```csharp
options.DataSource = yourDbDataSource;   // ConnectionString is then not required
```

Tracon never disposes an instance it did not build; the caller keeps
ownership. `Microsoft.Data.SqlClient` does not itself offer a `DbDataSource`
implementation (measured against version 7.0.2), so this is for a `DbDataSource`
adapter you write yourself — see `Tracon.PostgreSql`'s
`https://tracon.dev/guides/ef-core/` for the pattern.

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
`Tracon.PostgreSql` and are not meant to.

## Read contract view

Set `EnableReadViews = true` to publish `{schema}.runs_v1`, a narrow, versioned,
read-only view over run data — query it with your own SQL or map it as an EF Core
keyless entity, without depending on the internal `runs` table shape. Off by
default; a deployment that never turns it on never sees the object. See
[Read contract views](https://tracon.dev/reference/read-views/) for
the column list and the compatibility rule.

## AOT

This package is **not AOT-compatible**: `Microsoft.Data.SqlClient` is not marked
for trimming. For setups that need AOT, use `Tracon.PostgreSql`.

## Two providers at once

If `UsePostgreSql()` and `UseSqlServer()` are called in the same chain, **the last
registration wins** and a warning is logged at startup. This is a configuration
error; call only one of them.

## Links

- Guide: <https://tracon.dev/getting-started/persistence/>
- Capability map: <https://tracon.dev/capabilities/>
- API reference: <https://tracon.dev/api/>

Licence: PolyForm Small Business 1.0.0 - free below 100 people and 1,000,000 USD
(2019, inflation adjusted) revenue; a commercial licence applies above that. Terms
ship in the package as LICENSE.md. Details: <https://tracon.dev/reference/licensing/>
