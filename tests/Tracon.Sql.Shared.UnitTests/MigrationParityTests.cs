using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// Guards the two ways a column read by the shared stores can go missing: a
/// migration skipped in one provider, and a select list that drifts from the
/// ordinals the reader uses.
/// </summary>
/// <remarks>
/// <para>
/// The three providers ship independent, independently numbered migration sets
/// (their numbers do not line up and are not meant to). The store that reads
/// them is shared, so a column added to one set and forgotten in another fails
/// only at runtime, on whichever provider the developer did not have running.
/// </para>
/// <para>
/// Like <see cref="SqlTextSnapshotTests"/>, this project is deliberately used
/// rather than the three <c>*.IntegrationTests</c> projects: the check reads
/// embedded resources and query text, needs no connection, and would otherwise
/// pay for a Docker container per provider.
/// </para>
/// </remarks>
public sealed class MigrationParityTests
{
    private const string PostgresAndSqlServerSchema = "tracon";

    private const string SqliteTablePrefix = "tracon_";

    /// <summary>
    /// Every column the score reader selects is created by every provider.
    /// </summary>
    /// <remarks>
    /// <c>SqlRunScoreStore</c> addresses the row by bare ordinal, so a provider
    /// missing one of these columns does not return a shorter row — the query
    /// fails outright, and a later migration that re-adds the column in another
    /// position shifts every field after it.
    /// </remarks>
    [Fact]
    public void Every_column_the_score_reader_selects_exists_in_all_three_migration_sets()
        => AssertColumnsExistEverywhere(
            "run_scores",
            SelectedColumns(new PostgresQueries(PostgresAndSqlServerSchema).SelectRunScores));

    /// <summary>
    /// Every column the MCP server and webhook subscription readers select is
    /// created by every provider.
    /// </summary>
    /// <remarks>
    /// Both readers address the row by bare ordinal. Phase 190 appended
    /// <c>header_configuration_keys</c> to both lists; a provider that misses the
    /// migration fails every read of the table.
    /// </remarks>
    [Theory]
    [InlineData("mcp_servers")]
    [InlineData("webhook_subscriptions")]
    public void Every_column_the_mcp_and_webhook_readers_select_exists_in_all_three_migration_sets(string table)
        => AssertColumnsExistEverywhere(table, SelectedColumns(Select(table)));

    /// <summary>
    /// The write side of every dialect names the columns in the order the
    /// reader expects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🚨 This is the surface that can actually drift. The SELECT text is built
    /// once in the shared base, so comparing the three dialects' SELECT text
    /// would compare a constant with itself. The upserts are written out per
    /// dialect — three <c>VALUES</c> lists plus SQL Server's own <c>OUTPUT</c>
    /// column list — and each one carries the order by hand.
    /// </para>
    /// <para>
    /// The <c>OUTPUT</c> list is read up to the next <c>WHERE</c> or
    /// <c>VALUES</c>, not to the end of the line: the MCP server and webhook
    /// lists span several lines, and a one-line read compared only their first
    /// few columns.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("run_scores")]
    [InlineData("mcp_servers")]
    [InlineData("webhook_subscriptions")]
    public void Every_dialects_WRITE_names_the_columns_in_the_readers_order(string table)
    {
        var expected = SelectedColumns(Select(table));

        expected.ShouldNotBeEmpty();

        foreach (var (dialect, upsert) in Upserts(table))
        {
            // The @-parameter list of every VALUES clause in the statement.
            var valueLists = Regex
                .Matches(upsert, @"VALUES\s*\((?<columns>[^)]*)\)",
                    RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture,
                    TimeSpan.FromSeconds(5))
                .Select(match => Split(match.Groups["columns"].Value, trimLeading: '@'))
                .ToList();

            valueLists.ShouldNotBeEmpty($"{dialect} has no VALUES clause for '{table}'");

            foreach (var parameters in valueLists)
            {
                // The MCP upsert stamps both timestamps from one @now parameter.
                parameters
                    .Select((parameter, index) => string.Equals(parameter, "now", StringComparison.Ordinal)
                        && expected[index] is "created_at" or "updated_at"
                        ? expected[index]
                        : parameter)
                    .ShouldBe(expected, $"{dialect}'s VALUES list for '{table}' is out of step with the reader");
            }

            // SQL Server returns the row through OUTPUT inserted.* instead of
            // RETURNING, so that list carries the order by hand as well.
            foreach (Match output in Regex.Matches(
                upsert,
                @"OUTPUT\s+(?<columns>inserted\..*?)\s+(?=WHERE\b|VALUES\b)",
                RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Singleline,
                TimeSpan.FromSeconds(5)))
            {
                Split(output.Groups["columns"].Value, trimLeading: default)
                    .Select(static column => column.Replace("inserted.", string.Empty, StringComparison.Ordinal))
                    .ShouldBe(expected, $"{dialect}'s OUTPUT list for '{table}' is out of step with the reader");
            }
        }
    }

    /// <summary>
    /// The SQL Server upserts carry an <c>OUTPUT</c> list for every table the
    /// write test covers; a regex that stops matching would pass it vacuously.
    /// </summary>
    [Theory]
    [InlineData("run_scores")]
    [InlineData("mcp_servers")]
    [InlineData("webhook_subscriptions")]
    public void The_SQL_Server_OUTPUT_list_is_found(string table)
    {
        var upsert = Upserts(table).Single(static pair => string.Equals(pair.Dialect, "SqlServer", StringComparison.Ordinal)).Sql;

        Regex.Count(
                upsert,
                @"OUTPUT\s+(?<columns>inserted\..*?)\s+(?=WHERE\b|VALUES\b)",
                RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Singleline,
                TimeSpan.FromSeconds(5))
            .ShouldBe(2, "the UPDATE and the INSERT branch each carry one OUTPUT list");
    }

    private static string Select(string table)
    {
        var queries = new PostgresQueries(PostgresAndSqlServerSchema);

        return table switch
        {
            "run_scores" => queries.SelectRunScores,
            "mcp_servers" => queries.SelectMcpServers,
            "webhook_subscriptions" => queries.SelectWebhookSubscriptions,
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "No reader is known for the table."),
        };
    }

    private static void AssertColumnsExistEverywhere(string table, IReadOnlyList<string> columns)
    {
        columns.ShouldNotBeEmpty();

        var missing = new List<string>();

        foreach (var (provider, sql) in MigrationSets())
        {
            var statements = StatementsTouching(sql, table);

            statements.ShouldNotBeNullOrWhiteSpace($"{provider} has no migration statement for '{table}'");

            foreach (var column in columns)
            {
                if (!Regex.IsMatch(statements, $@"\b{Regex.Escape(column)}\b", RegexOptions.None, TimeSpan.FromSeconds(5)))
                {
                    missing.Add($"{provider}: {table}.{column}");
                }
            }
        }

        missing.ShouldBeEmpty(
            $"every provider must create every column the shared reader selects from '{table}'");
    }

    /// <summary>
    /// The text of the statements that create or alter <paramref name="table"/>.
    /// </summary>
    /// <remarks>
    /// 🚨 Scoped to the table on purpose. Searching the whole migration set for a
    /// bare column name makes the check pass on words that merely appear
    /// somewhere else — `status`, `state` and `key` occur in every provider's
    /// set regardless of which tables carry them, so an unscoped search would be
    /// green for almost any column.
    /// </remarks>
    private static string StatementsTouching(string sql, string table)
    {
        var builder = new StringBuilder();

        // 🚨 Line comments are stripped FIRST, for two reasons. A `;` inside a
        // comment would cut a CREATE TABLE short and silently drop every column
        // below it (measured: 0017_run_scores.sql loses six), and comment prose
        // must not count as evidence that a column exists.
        sql = Regex.Replace(sql, "--[^\r\n]*", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(5));

        // The table is written as {schema}.run_scores (PostgreSQL, SQL Server)
        // or {schema}run_scores (SQLite, K-193), so the qualifier is optional.
        var pattern = $@"(?:CREATE\s+TABLE|ALTER\s+TABLE)[^;]*?\{{schema\}}\.?{Regex.Escape(table)}\b[^;]*;";

        foreach (Match match in Regex.Matches(
            sql, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(5)))
        {
            builder.AppendLine(match.Value);
        }

        return builder.ToString();
    }

    /// <summary>Reads the column list out of a resolved <c>SELECT ... FROM</c>.</summary>
    private static IReadOnlyList<string> SelectedColumns(string selectStatement)
    {
        var select = selectStatement.IndexOf("SELECT", StringComparison.Ordinal);
        var from = selectStatement.IndexOf("FROM", StringComparison.Ordinal);

        select.ShouldBeGreaterThanOrEqualTo(0);
        from.ShouldBeGreaterThan(select);

        return Split(selectStatement[(select + "SELECT".Length)..from], trimLeading: default);
    }

    private static IReadOnlyList<string> Split(string list, char trimLeading)
        => [.. list
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => trimLeading == default ? entry : entry.TrimStart(trimLeading))];

    /// <summary>The per-dialect upsert text, which carries the order by hand.</summary>
    /// <remarks>
    /// Each provider compiles its own copy of the shared base (linked source,
    /// K-176), so the three query types share no common type to switch over.
    /// </remarks>
    private static IEnumerable<(string Dialect, string Sql)> Upserts(string table)
    {
        var postgres = new PostgresQueries(PostgresAndSqlServerSchema);
        var sqlServer = new SqlServerQueries(PostgresAndSqlServerSchema);
        var sqlite = new SqliteQueries(SqliteTablePrefix);

        return table switch
        {
            "run_scores" =>
            [
                ("PostgreSql", postgres.UpsertRunScore),
                ("SqlServer", sqlServer.UpsertRunScore),
                ("Sqlite", sqlite.UpsertRunScore),
            ],
            "mcp_servers" =>
            [
                ("PostgreSql", postgres.UpsertMcpServer),
                ("SqlServer", sqlServer.UpsertMcpServer),
                ("Sqlite", sqlite.UpsertMcpServer),
            ],
            "webhook_subscriptions" =>
            [
                ("PostgreSql", postgres.UpsertWebhookSubscription),
                ("SqlServer", sqlServer.UpsertWebhookSubscription),
                ("Sqlite", sqlite.UpsertWebhookSubscription),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "No upsert is known for the table."),
        };
    }

    /// <summary>The concatenated migration text each provider embeds.</summary>
    private static IEnumerable<(string Provider, string Sql)> MigrationSets()
    {
        yield return ("PostgreSql", EmbeddedMigrations(typeof(PostgresQueries).Assembly));
        yield return ("SqlServer", EmbeddedMigrations(typeof(SqlServerQueries).Assembly));
        yield return ("Sqlite", EmbeddedMigrations(typeof(SqliteQueries).Assembly));
    }

    private static string EmbeddedMigrations(Assembly assembly)
    {
        var builder = new StringBuilder();

        // Only the mandatory set: the optional sets (pgvector, read views) are
        // not applied to every deployment, so a column that lives only there is
        // not a column the shared reader may select.
        foreach (var name in assembly
            .GetManifestResourceNames()
            .Where(static name => name.Contains(".Migrations.", StringComparison.Ordinal))
            .Where(static name => name.EndsWith(".sql", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            builder.AppendLine(reader.ReadToEnd());
        }

        builder.Length.ShouldBeGreaterThan(0, $"{assembly.GetName().Name} embeds no migration files");

        return builder.ToString();
    }
}
