using System.Data.Common;
using System.Globalization;
using System.Text;

namespace Tracon.UpgradeGate;

/// <summary>A database the upgrade gate builds, fills and upgrades; one per provider, supplied by the provider's test project.</summary>
internal interface IUpgradeDatabase : IAsyncDisposable
{
    /// <summary>Gets the provider.</summary>
    SeedProvider Provider { get; }

    /// <summary>Gets the name of the provider's directory in the baseline manifest (<c>sqlite</c>, <c>postgresql</c>, <c>sqlserver</c>).</summary>
    string ProviderDirectory { get; }

    /// <summary>Gets the shared store context, which the stores and the runner are built on.</summary>
    SqlStoreContext StoreContext { get; }

    /// <summary>Gets the tenant the stores run as.</summary>
    ITenantContext TenantContext { get; }

    /// <summary>Gets the migration runner of the context.</summary>
    MigrationRunner Migrations { get; }

    /// <summary>Gets the schema name (PostgreSQL, SQL Server) or the table prefix (SQLite).</summary>
    string Schema { get; }

    /// <summary>Names a table the way the provider addresses it.</summary>
    /// <param name="table">The table.</param>
    /// <returns>The qualified name.</returns>
    string Qualify(string table);
}

/// <summary>What the seed wrote into a database.</summary>
/// <param name="Rows">The number of rows written, by table; a table the schema did not have yet is absent.</param>
/// <param name="Columns">The columns each of those tables had at that moment.</param>
/// <param name="OrdinalKeys">Whether the keys a client chooses compared by their characters at that moment.</param>
internal sealed record SeedOutcome(
    IReadOnlyDictionary<string, int> Rows,
    IReadOnlyDictionary<string, IReadOnlySet<string>> Columns,
    bool OrdinalKeys);

/// <summary>The steps the gate takes on a database, provider by provider.</summary>
internal static class UpgradeHarness
{
    private const int DeadlockAttempts = 8;

    /// <summary>Reads the migrations the tree holds for the provider, in order.</summary>
    /// <param name="database">The database.</param>
    /// <returns>The migrations.</returns>
    public static IReadOnlyList<MigrationDescriptor> HeadMigrations(IUpgradeDatabase database)
    {
        var dialect = database.StoreContext.Dialect;

        return MigrationDescriptor.Discover(dialect.GetType().Assembly, dialect.MigrationResourcePrefix);
    }

    /// <summary>Reads the migrations the tree holds for one optional set of the provider, in order.</summary>
    /// <param name="database">The database.</param>
    /// <param name="setName">The set (<c>views</c>, <c>knowledge</c>).</param>
    /// <returns>The migrations.</returns>
    public static IReadOnlyList<MigrationDescriptor> OptionalMigrations(IUpgradeDatabase database, string setName)
    {
        var dialect = database.StoreContext.Dialect;

        return MigrationDescriptor.Discover(dialect.GetType().Assembly, dialect.OptionalMigrationResourcePrefixes[setName]);
    }

    /// <summary>
    /// Brings a database to the state a host that applied exactly these migrations leaves: the schema, and a ledger line
    /// per migration that carries the checksum the caller names.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="migrations">The migrations to apply, in order.</param>
    /// <param name="ledgerChecksum">The checksum to write into the ledger for a migration.</param>
    /// <param name="setName">The migration set the files belong to (<c>core</c>, or an optional set such as <c>views</c>).</param>
    /// <returns>The completion task.</returns>
    public static async ValueTask ApplyAsync(
        IUpgradeDatabase database,
        IEnumerable<MigrationDescriptor> migrations,
        Func<MigrationDescriptor, string> ledgerChecksum,
        string setName = "core")
    {
        var context = database.StoreContext;
        var sql = context.Sql;

        await ExecuteAsync(database, sql.CreateSchema).ConfigureAwait(false);
        await ExecuteAsync(database, sql.CreateMigrationsTable).ConfigureAwait(false);

        var unicode = database.Provider == SeedProvider.SqlServer ? "N" : string.Empty;

        foreach (var migration in migrations)
        {
            // The template values a set asks for (the vector dimension of the knowledge set) are what the runner substitutes.
            var text = sql.ApplySchema(migration.Sql);

            foreach (var (key, value) in context.MigrationTemplateValues)
            {
                text = text.Replace("{" + key + "}", value, StringComparison.Ordinal);
            }

            await ExecuteAsync(database, text).ConfigureAwait(false);

            var appliedAt = UpgradeSeed.Render(database.Provider, new SeedValue(SeedKind.Timestamp, "2026-03-01T09:00:00Z"));

            await ExecuteAsync(
                database,
                $"INSERT INTO {database.Qualify("__migrations")} (set_name, id, name, checksum, applied_at) " +
                $"VALUES ({unicode}'{setName}', {migration.Id.ToString(CultureInfo.InvariantCulture)}, {unicode}'{migration.Name}', " +
                $"{unicode}'{ledgerChecksum(migration)}', {appliedAt});").ConfigureAwait(false);
        }
    }

    /// <summary>Runs a statement, retrying a deadlock victim the way the migration runner does.</summary>
    /// <param name="database">The database.</param>
    /// <param name="sql">The statement.</param>
    /// <returns>The completion task.</returns>
    public static async ValueTask ExecuteAsync(IUpgradeDatabase database, string sql)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var command = database.StoreContext.CreateCommand(sql);
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);

                return;
            }
            catch (DbException exception) when (attempt < DeadlockAttempts && database.StoreContext.Dialect.IsDeadlock(exception))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt)).ConfigureAwait(false); // delay: retry
            }
        }
    }

    /// <summary>Reads the first column of every row a query returns.</summary>
    /// <param name="database">The database.</param>
    /// <param name="sql">The query.</param>
    /// <returns>The values as text.</returns>
    public static async ValueTask<List<string>> QueryColumnAsync(IUpgradeDatabase database, string sql)
    {
        await using var command = database.StoreContext.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

        var values = new List<string>();

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            values.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
        }

        return values;
    }

    /// <summary>Lists the tables of the schema the gate works in, without the migration ledger.</summary>
    /// <param name="database">The database.</param>
    /// <returns>The table names, as the migrations spell them.</returns>
    public static async ValueTask<HashSet<string>> TablesAsync(IUpgradeDatabase database)
    {
        var schema = database.Schema;

        var names = database.Provider switch
        {
            SeedProvider.Sqlite => (await QueryColumnAsync(
                database,
                $"SELECT substr(name, {(schema.Length + 1).ToString(CultureInfo.InvariantCulture)}) FROM sqlite_master " +
                $"WHERE type = 'table' AND substr(name, 1, {schema.Length.ToString(CultureInfo.InvariantCulture)}) = '{schema}';").ConfigureAwait(false)),
            SeedProvider.PostgreSql => await QueryColumnAsync(
                database,
                $"SELECT table_name FROM information_schema.tables WHERE table_schema = '{schema}' AND table_type = 'BASE TABLE';").ConfigureAwait(false),
            _ => await QueryColumnAsync(
                database,
                "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; " +
                $"SELECT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = N'{schema}';").ConfigureAwait(false),
        };

        return [.. names.Where(static name => !string.Equals(name, "__migrations", StringComparison.Ordinal))];
    }

    /// <summary>Lists the columns a table has now.</summary>
    /// <param name="database">The database.</param>
    /// <param name="table">The table.</param>
    /// <returns>The column names.</returns>
    public static async ValueTask<HashSet<string>> ColumnsAsync(IUpgradeDatabase database, string table)
    {
        var schema = database.Schema;

        var names = database.Provider switch
        {
            SeedProvider.Sqlite => await QueryColumnAsync(database, $"SELECT name FROM pragma_table_info('{schema}{table}');").ConfigureAwait(false),
            SeedProvider.PostgreSql => await QueryColumnAsync(
                database,
                $"SELECT column_name FROM information_schema.columns WHERE table_schema = '{schema}' AND table_name = '{table}';").ConfigureAwait(false),
            _ => await QueryColumnAsync(
                database,
                "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; " +
                $"SELECT c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'{schema}.{table}');").ConfigureAwait(false),
        };

        return [.. names];
    }

    /// <summary>Counts the rows of every table of the schema.</summary>
    /// <param name="database">The database.</param>
    /// <returns>The row count by table.</returns>
    public static async ValueTask<SortedDictionary<string, long>> CountsAsync(IUpgradeDatabase database)
    {
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);

        foreach (var table in await TablesAsync(database).ConfigureAwait(false))
        {
            var rows = await QueryColumnAsync(database, $"SELECT COUNT(*) FROM {database.Qualify(table)};").ConfigureAwait(false);

            counts[table] = long.Parse(rows.Single(), CultureInfo.InvariantCulture);
        }

        return counts;
    }

    /// <summary>
    /// Writes the seed into the tables the schema has at this moment, with the columns each of them has at this moment.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="seed">The seed.</param>
    /// <returns>What was written; a table the schema does not have yet is absent.</returns>
    /// <exception cref="InvalidOperationException">A row did not fit its table; the message names every such table with the error of the database.</exception>
    public static async ValueTask<SeedOutcome> SeedAsync(IUpgradeDatabase database, UpgradeSeed seed)
    {
        var tables = await TablesAsync(database).ConfigureAwait(false);
        var written = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var seededColumns = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        var misfits = new List<string>();
        var ordinalKeys = await KeysAreOrdinalAsync(database, tables).ConfigureAwait(false);

        foreach (var (table, rows) in seed.Tables)
        {
            if (!tables.Contains(table))
            {
                continue;
            }

            var columns = await ColumnsAsync(database, table).ConfigureAwait(false);

            seededColumns[table] = columns;

            foreach (var row in rows)
            {
                if (row.NeedsOrdinalKeys && !ordinalKeys)
                {
                    continue;
                }

                var statement = UpgradeSeed.RenderInsert(database.Provider, database.Qualify(table), row, columns);

                if (statement is null)
                {
                    continue;
                }

                try
                {
                    await ExecuteAsync(database, statement).ConfigureAwait(false);
                }
                catch (DbException exception)
                {
                    misfits.Add($"  {table} (columns now: {string.Join(", ", columns.Order(StringComparer.Ordinal))}): {exception.Message.ReplaceLineEndings(" ")}");

                    continue;
                }

                written[table] = written.GetValueOrDefault(table) + 1;
            }
        }

        if (misfits.Count > 0)
        {
            throw new InvalidOperationException(
                $"{misfits.Count} seed row(s) do not fit the schema they were written for:{Environment.NewLine}{string.Join(Environment.NewLine, misfits)}");
        }

        return new SeedOutcome(written, seededColumns, ordinalKeys);
    }

    /// <summary>
    /// Writes the one row the optional sets add to the seed: a vector the knowledge set (PostgreSQL only) keeps. The read views
    /// hold no row of their own: they read the runs of the core seed.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <returns>The completion task.</returns>
    public static async ValueTask SeedOptionalAsync(IUpgradeDatabase database)
    {
        if (database.Provider != SeedProvider.PostgreSql)
        {
            return;
        }

        await ExecuteAsync(
            database,
            $"INSERT INTO {database.Qualify("document_embeddings")} " +
            "(id, tenant_id, collection, source_id, chunk_index, content, metadata, embedding, created_at) " +
            $"VALUES ('{Guid.NewGuid():D}', 'tenant-a', 'docs', 'source-1', 0, 'what the release wrote', '{{}}'::jsonb, '{VectorLiteral(database)}', now());").ConfigureAwait(false);
    }

    /// <summary>
    /// Reads what the optional sets hold after the upgrade: the read view lists every run the core tables hold, and the
    /// vector the release wrote is still there, as it was.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="before">The row counts before the upgrade.</param>
    /// <returns>The completion task.</returns>
    public static async ValueTask AssertOptionalSetsReadAsync(IUpgradeDatabase database, IReadOnlyDictionary<string, long> before)
    {
        // Every column of the view is evaluated by the server whether the client reads it or not; an upgrade that left the view over
        // a column it no longer has fails here, not in the operator's dashboard.
        var viewRows = await QueryColumnAsync(database, $"SELECT * FROM {database.Qualify("runs_v1")};").ConfigureAwait(false);

        viewRows.Count.ShouldBe((int)before["runs"], "the runs_v1 view lists every run the runs table holds");

        if (database.Provider != SeedProvider.PostgreSql)
        {
            return;
        }

        var kept = await QueryColumnAsync(database, $"SELECT content || '|' || embedding::text FROM {database.Qualify("document_embeddings")};").ConfigureAwait(false);

        kept.ShouldBe([$"what the release wrote|{VectorLiteral(database)}"], "the vector the release wrote reads back as it was");
    }

    private static string VectorLiteral(IUpgradeDatabase database)
    {
        var dimension = int.Parse(database.StoreContext.MigrationTemplateValues.GetValueOrDefault("dimension", "3"), CultureInfo.InvariantCulture);

        return "[" + string.Join(",", Enumerable.Range(1, dimension).Select(static index => (index / 10.0).ToString("0.0", CultureInfo.InvariantCulture))) + "]";
    }

    /// <summary>
    /// Whether a key a client chooses compares by its characters here: always on PostgreSQL and SQLite, and on SQL Server
    /// once its key columns carry a binary collation.
    /// </summary>
    private static async ValueTask<bool> KeysAreOrdinalAsync(IUpgradeDatabase database, HashSet<string> tables)
    {
        if (database.Provider != SeedProvider.SqlServer || !tables.Contains("idempotency_keys"))
        {
            return true;
        }

        var collation = await QueryColumnAsync(
            database,
            "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; " +
            $"SELECT c.collation_name FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'{database.Schema}.idempotency_keys') AND c.name = N'key';").ConfigureAwait(false);

        return collation.Single().Contains("BIN", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Formats the difference between two row counts, empty when there is none.</summary>
    /// <param name="before">The counts before.</param>
    /// <param name="after">The counts after.</param>
    /// <returns>One line per table whose count changed.</returns>
    public static string DescribeCountChanges(IReadOnlyDictionary<string, long> before, IReadOnlyDictionary<string, long> after)
    {
        var builder = new StringBuilder();

        foreach (var (table, count) in before)
        {
            var now = after.GetValueOrDefault(table, -1);

            if (now != count)
            {
                builder.Append(CultureInfo.InvariantCulture, $"  {table}: {count} rows before, {(now < 0 ? "the table is gone" : now + " after")}{Environment.NewLine}");
            }
        }

        return builder.ToString();
    }
}
