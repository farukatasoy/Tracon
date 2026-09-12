# Tracon.PostgreSql

PostgreSQL persistence for Tracon, and the only provider that also carries vector
search.

```bash
dotnet add package Tracon.PostgreSql
```

```csharp
builder.AddTracon()
       .UsePostgreSql(connectionString);
```

That single call replaces every in-memory store: agent definitions and their version
history, sessions, conversations, runs and run events, jobs and schedules, evals,
experiments, quotas, webhooks, attachments, the audit trail, and API keys.

## It stays out of your database

Everything lives in a separate schema — `tracon` by default. Your `public` schema
is never touched, and no table name can collide with one of yours.

```csharp
.UsePostgreSql(options =>
{
    options.ConnectionString = connectionString;
    options.SchemaName = "tracon";   // must be a valid unquoted SQL identifier
    options.AutoApplyMigrations = true;
    options.CommandTimeoutSeconds = 30;
})
```

Settings can also be bound from configuration, which is the usual shape: the
connection string is a secret and belongs in `dotnet user-secrets` or the
environment, never in `appsettings.json`.

## Sharing a connection pool with your own `NpgsqlDataSource`

Give `DataSource` instead of `ConnectionString` when your host already builds its
own `NpgsqlDataSource` — for an EF Core `DbContext`, for example — and you want
Tracon's traffic on that same pool:

```csharp
var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();

.UsePostgreSql(options => options.DataSource = dataSource)
```

Tracon never disposes an instance it did not build; the caller keeps ownership.
Building two separate `NpgsqlDataSource` instances from an identical connection
string does **not** share a pool — Npgsql pools per instance, not per string — so
this is the only way to actually share one. Full pattern, including EF Core:
<https://tracon.dev/guides/ef-core/>.

## Migrations

29 SQL migrations ship embedded in the assembly and are applied at startup by
default. Two properties make that safe to run from every instance of a scaled-out
application:

- The run is wrapped in a **`pg_advisory_lock`** keyed on the schema name, so several
  instances starting at once serialize instead of racing.
- Each applied file's SHA-256 is recorded. If a file's content later differs from
  what was applied, startup **fails loudly** rather than continuing against a schema
  that is not what the code expects.

> That checksum covers the file's whole text, comments included. Editing an
> applied migration is not supported — add a new one. If you must take a change to an
> already-applied file, drop and recreate the schema in that environment first.

Set `AutoApplyMigrations = false` when your deployment applies schema changes as its
own step; Tracon then verifies but does not write. `GET /api/diagnostics` reports
whether the schema is up to date and lists anything pending.

## Vector search

Migration `0024` creates the `vector` extension and a `document_embeddings` table with
an **HNSW** index over cosine distance. This is what backs the knowledge endpoints —
document upload, source listing, and semantic search — and it is why those endpoints
answer `501` on any other provider.

The embedding dimension is fixed when the table is created, because a vector column's
width is part of its type. Changing embedding models later means a new migration, not
a configuration change.

You need PostgreSQL with `pgvector` available. Without it the extension cannot be
created and that migration fails; if you do not need knowledge search, the other
providers do everything else.

## Read contract view

Set `EnableReadViews = true` to publish `{schema}.runs_v1`, a narrow, versioned,
read-only view over run data — query it with your own SQL or map it as an EF Core
keyless entity, without depending on the internal `runs` table shape. Off by
default; a deployment that never turns it on never sees the object. See
[Read contract views](https://tracon.dev/reference/read-views/) for
the column list and the compatibility rule.

## Choosing a provider

| Package | Use it when |
|---|---|
| **Tracon.PostgreSql** | The default. The only one with vector search |
| `Tracon.SqlServer` | You already run SQL Server |
| `Tracon.Sqlite` | Single node, or a durable local development setup |

All three implement the same contracts and are verified against the same shared
contract test suite, so the choice does not change your code.

## Compatibility

Targets `net8.0`, `net9.0`, and `net10.0`. Trimming- and AOT-compatible. Uses `Npgsql`
directly — there is no ORM, and no Entity Framework dependency enters your graph.

## Links

- Full documentation: <https://tracon.dev>
- API reference: <https://tracon.dev/api/>

License: PolyForm Small Business 1.0.0 - free below 100 people and 1,000,000 USD
(2019, inflation adjusted) revenue; a commercial licence applies above that. Terms
ship in the package as LICENSE.md. Details: <https://tracon.dev/reference/licensing/>
