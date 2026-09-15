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
    /// The write side of every dialect names the score columns in the order the
    /// reader expects.
    /// </summary>
    /// <remarks>
    /// 🚨 This is the surface that can actually drift. <c>SelectRunScores</c> is
    /// built once in the shared base, so comparing the three dialects' SELECT
    /// text would compare a constant with itself. <c>UpsertRunScore</c> is
    /// written out per dialect — three <c>VALUES</c> lists plus SQL Server's own
    /// <c>OUTPUT</c> column list — and each one carries the order by hand.
    /// </remarks>
    [Fact]
    public void Every_dialects_score_WRITE_names_the_columns_in_the_readers_order()
    {
        var expected = SelectedColumns(new PostgresQueries(PostgresAndSqlServerSchema).SelectRunScores);

        expected.ShouldNotBeEmpty();

        foreach (var (dialect, upsert) in ScoreUpserts())
        {
            // The @-parameter list of every VALUES clause in the statement.
            var valueLists = Regex
                .Matches(upsert, @"VALUES\s*\((?<columns>[^)]*)\)",
                    RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture,
                    TimeSpan.FromSeconds(5))
                .Select(match => Split(match.Groups["columns"].Value, trimLeading: '@'))
                .ToList();

            valueLists.ShouldNotBeEmpty($"{dialect} has no VALUES clause for a score");

            foreach (var parameters in valueLists)
            {
                parameters.ShouldBe(expected, $"{dialect}'s VALUES list is out of step with the reader");
            }

            // SQL Server returns the row through OUTPUT inserted.* instead of
            // RETURNING, so that list carries the order by hand as well.
            foreach (Match output in Regex.Matches(
                upsert,
                @"OUTPUT\s+(?<columns>inserted\.[^\r\n]+)",
                RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture,
                TimeSpan.FromSeconds(5)))
            {
                Split(output.Groups["columns"].Value, trimLeading: default)
                    .Select(static column => column.Replace("inserted.", string.Empty, StringComparison.Ordinal))
                    .ShouldBe(expected, $"{dialect}'s OUTPUT list is out of step with the reader");
            }
        }
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

    /// <summary>The per-dialect score upsert text, which carries the order by hand.</summary>
    private static IEnumerable<(string Dialect, string Sql)> ScoreUpserts()
    {
        yield return ("PostgreSql", new PostgresQueries(PostgresAndSqlServerSchema).UpsertRunScore);
        yield return ("SqlServer", new SqlServerQueries(PostgresAndSqlServerSchema).UpsertRunScore);
        yield return ("Sqlite", new SqliteQueries(SqliteTablePrefix).UpsertRunScore);
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
