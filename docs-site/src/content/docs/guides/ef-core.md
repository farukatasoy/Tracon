---
title: Two connection planes — EF Core and Tracon
description: Share a PostgreSQL connection pool with your own EF Core DbContext without giving Tracon a dependency on Entity Framework.
---

Tracon does not use Entity Framework Core internally, and does not ship an EF
Core package. Its store layer talks to the database through raw ADO.NET
(`DbDataSource`, `DbCommand`) on purpose — that keeps `Tracon.PostgreSql` AOT
compatible and keeps the store contract at the same abstraction level EF Core
itself sits at, so there is nothing an EF integration would add. If your
application already uses EF Core for its own schema, this page is about the
connection plane the two share, not about making Tracon use EF Core.

## One data source, two consumers

Give both sides the exact same `NpgsqlDataSource` instance and they share one real
connection pool — building two data sources from an identical connection string
does **not** do this; see
[Two data planes, one connection pool or two](/guides/embedding/#two-data-planes-one-connection-pool-or-two)
for the measurement:

```csharp
var dataSource = new NpgsqlDataSourceBuilder(connectionString)
    // Token refresh, client certificates, custom type mappings: anything not
    // expressible in a connection string now applies to Tracon's traffic too.
    .Build();

builder.Services.AddDbContext<YourDbContext>(o => o.UseNpgsql(dataSource));

builder.AddTracon()
       .UsePostgreSql(o => o.DataSource = dataSource);
```

`TraconPostgreSqlOptions.ConnectionString` is not required when `DataSource` is
set — giving both at once is a startup error, not a silent preference. Tracon
never disposes an instance it did not build: ownership stays with whoever created
it (your host, in this example), and it keeps working after Tracon's own
`ServiceProvider` shuts down.

The same seam exists on `TraconSqlServerOptions.DataSource` and
`TraconSqliteOptions.DataSource`, for symmetry. Neither
`Microsoft.Data.SqlClient` nor `Microsoft.Data.Sqlite` ships a `DbDataSource`
implementation of its own today (measured against `Microsoft.Data.SqlClient
7.0.2`), so there is no equivalent "give EF Core's data source to Tracon" call
for those two providers yet — the field is ready for the day the ecosystem catches
up, or for a `DbDataSource` adapter you write yourself.

## There is no shared transaction

`YourDbContext.SaveChangesAsync()` and an Tracon run write through two
independent connections, even when they share one pool. A run's recording never
becomes atomic with your own schema's writes, and this is deliberate: Tracon's
stores are singletons that outlive any one request, and "observability must never
block the run" is a standing rule — a run's own stores never wait on your
transaction to commit, and yours never waits on Tracon's.

## Reference a run by `RunId`, not by copying its data

Point your own entity at the run instead of duplicating what Tracon already
recorded:

```csharp
public sealed class SupportTicket
{
    public Guid Id { get; set; }
    public required string RunId { get; set; }   // reference, not a copy
    // ... your own domain fields
}
```

Read the run's own data back through `IRunStore`/`GET /api/runs/{id}` when you need
it; do not copy fields across at write time — the two writes are not atomic (above),
so a copy can drift the moment either side fails after the other commits. If your
own write can retry, protect it with `Idempotency-Key`
(see [Make supported HTTP submissions idempotent](/guides/reliability/#make-supported-http-submissions-idempotent))
the same way any other retryable write in front of Tracon would be.

## Two migration steps, one order that matters less than you think

Your own `dotnet ef database update` and Tracon's `tracon migrate` apply to
two schemas with no foreign key between them (`tracon` versus yours), so the
order they run in does not matter. What does matter is running both with
automatic migration off, so exactly one mechanism touches the database at deploy
time:

```bash
dotnet ef database update
tracon migrate --provider postgres --connection "$TRACON_CONNECTION"
```

See [Choose a migration strategy](/guides/production/#choose-a-migration-strategy)
for `AutoApplyMigrations` and the rest of the deployment-pipeline shape.

## Reading through a keyless entity instead

The two connection planes above are about your own writes. When you only need to
**read** run data next to your own entity — a cost column in your own report, a
status filter in your own dashboard — a third, narrower option exists: `runs_v1`,
a versioned, read-only SQL view opt in with `EnableReadViews`. Map it as a keyless
entity and query it with LINQ, without giving Tracon's internal `runs` table
shape a dependency on your code:

```csharp
builder.AddTracon()
       .UsePostgreSql(o =>
       {
           o.ConnectionString = connectionString;
           o.EnableReadViews = true;
       });
```

```csharp
modelBuilder.Entity<TraconRun>()
    .HasNoKey()
    .ToView("runs_v1", "tracon");
```

See [Read contract views](/reference/read-views/) for the full column list, the
`total_cost` null-preserving rule, and why the view is not a tenant boundary.

## What you do not get

- **No EF Core global query filter** reaches Tracon's rows — its own tenant
  isolation runs through its own `tenant_id` column and `ITenantContext`, a
  separate mechanism from EF Core's.
- **`dotnet ef migrations` never sees Tracon's schema.** It is not an EF model;
  there is no `DbSet` to scaffold it from, and none should be added.
- **No `DbContext` owns an Tracon table.** The `tracon` schema is written
  only by Tracon's own store layer, from either connection plane.

## Read next

- [Read contract views](/reference/read-views/) — the full `runs_v1` column list and compatibility rule
- [Embedding into a host application](/guides/embedding/) — the two data planes and connection pool sharing measurement
- [Production deployment](/guides/production/) — the migration strategy this page's CI snippet belongs to
