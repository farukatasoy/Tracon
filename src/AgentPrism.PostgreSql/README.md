# AgentPrism.PostgreSql

PostgreSQL persistence for AgentPrism, and the only provider that also carries vector
search.

```bash
dotnet add package AgentPrism.PostgreSql
```

```csharp
builder.AddAgentPrism()
       .UsePostgreSql(connectionString);
```

That single call replaces every in-memory store: agent definitions and their version
history, sessions, conversations, runs and run events, jobs and schedules, evals,
experiments, quotas, webhooks, attachments, the audit trail, and API keys.

## It stays out of your database

Everything lives in a separate schema — `agentprism` by default. Your `public` schema
is never touched, and no table name can collide with one of yours.

```csharp
.UsePostgreSql(options =>
{
    options.ConnectionString = connectionString;
    options.SchemaName = "agentprism";   // must be a valid unquoted SQL identifier
    options.AutoApplyMigrations = true;
    options.CommandTimeoutSeconds = 30;
})
```

Settings can also be bound from configuration, which is the usual shape: the
connection string is a secret and belongs in `dotnet user-secrets` or the
environment, never in `appsettings.json`.

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
own step; AgentPrism then verifies but does not write. `GET /api/diagnostics` reports
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

## Choosing a provider

| Package | Use it when |
|---|---|
| **AgentPrism.PostgreSql** | The default. The only one with vector search |
| `AgentPrism.SqlServer` | You already run SQL Server |
| `AgentPrism.Sqlite` | Single node, or a durable local development setup |

All three implement the same contracts and are verified against the same shared
contract test suite, so the choice does not change your code.

## Compatibility

Targets `net8.0`, `net9.0`, and `net10.0`. Trimming- and AOT-compatible. Uses `Npgsql`
directly — there is no ORM, and no Entity Framework dependency enters your graph.

## Links

- Full documentation: <https://agentprism.doayen.web.tr>
- API reference: <https://agentprism.doayen.web.tr/api/>

License: MIT
