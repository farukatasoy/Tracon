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
internal sealed record SeedOutcome(
    IReadOnlyDictionary<string, int> Rows,
    IReadOnlyDictionary<string, IReadOnlySet<string>> Columns);

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

    /// <summary>
    /// Brings a database to the state a host that applied exactly these migrations leaves: the schema, and a ledger line
    /// per migration that carries the checksum the caller names.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="migrations">The migrations to apply, in order.</param>
    /// <param name="ledgerChecksum">The checksum to write into the ledger for a migration.</param>
    /// <returns>The completion task.</returns>
    public static async ValueTask ApplyAsync(
        IUpgradeDatabase database,
        IEnumerable<MigrationDescriptor> migrations,
        Func<MigrationDescriptor, string> ledgerChecksum)
    {
        var context = database.StoreContext;
        var sql = context.Sql;

        await ExecuteAsync(database, sql.CreateSchema).ConfigureAwait(false);
        await ExecuteAsync(database, sql.CreateMigrationsTable).ConfigureAwait(false);

        var unicode = database.Provider == SeedProvider.SqlServer ? "N" : string.Empty;

        foreach (var migration in migrations)
        {
            await ExecuteAsync(database, sql.ApplySchema(migration.Sql)).ConfigureAwait(false);

            var appliedAt = UpgradeSeed.Render(database.Provider, new SeedValue(SeedKind.Timestamp, "2026-03-01T09:00:00Z"));

            await ExecuteAsync(
                database,
                $"INSERT INTO {database.Qualify("__migrations")} (set_name, id, name, checksum, applied_at) " +
                $"VALUES ({unicode}'core', {migration.Id.ToString(CultureInfo.InvariantCulture)}, {unicode}'{migration.Name}', " +
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
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt)).ConfigureAwait(false);
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
    /// <exception cref="InvalidOperationException">A row did not fit the table; the message names the table and carries the statement.</exception>
    public static async ValueTask<SeedOutcome> SeedAsync(IUpgradeDatabase database, UpgradeSeed seed)
    {
        var tables = await TablesAsync(database).ConfigureAwait(false);
        var written = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var seededColumns = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

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
                    throw new InvalidOperationException(
                        $"The seed row does not fit '{table}' (columns now: {string.Join(", ", columns.Order(StringComparer.Ordinal))}): {exception.Message}{Environment.NewLine}{statement}",
                        exception);
                }

                written[table] = written.GetValueOrDefault(table) + 1;
            }
        }

        return new SeedOutcome(written, seededColumns);
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
