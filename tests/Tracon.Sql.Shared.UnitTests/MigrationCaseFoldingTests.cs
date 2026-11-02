using System.Text;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// A migration that lowers or uppers a value says which rules it means.
/// </summary>
/// <remarks>
/// <para>
/// <c>lower('I')</c> is <c>i</c> in most databases and the dotless i (U+0131) in a database whose locale is Turkish.
/// A migration that rewrites a value with it, or looks one up with it, then does something else on a Turkish
/// database than anywhere else: <c>OpenAI</c> ended in a dotless i, and <c>AUTHORIZATION</c> was no longer found by
/// <c>lower(key) = 'authorization'</c>. Every other test of this repository runs on a database that is not Turkish,
/// so none of them can see it; this one reads the files.
/// </para>
/// <para>
/// PostgreSQL says <c>lower(x COLLATE "C")</c> and SQL Server <c>LOWER(x COLLATE Latin1_General_BIN2)</c>. Both fold
/// the ASCII letters and nothing else, on every database. SQLite is left out: its <c>lower()</c> folds ASCII only
/// already. This project reads the migrations as embedded resources and needs no database.
/// </para>
/// </remarks>
public sealed class MigrationCaseFoldingTests
{
    [Fact]
    public void Every_lower_and_upper_in_a_migration_names_an_ordinal_collation()
    {
        var offenders = new List<string>();
        var seen = 0;

        foreach (var (provider, name, sql) in MigrationFiles())
        {
            foreach (var call in SqlCaseFoldingScanner.Scan(sql, bracketIdentifiers: IsSqlServer(provider)))
            {
                seen++;

                if (!call.NamesOrdinalCollation)
                {
                    offenders.Add($"{provider}/{name}: {call.Text}");
                }
            }
        }

        seen.ShouldBeGreaterThan(0, "the scan found no lower() or upper() at all, so it would pass on nothing");

        offenders.ShouldBeEmpty(
            "lower() and upper() follow the locale of the database: on a Turkish database 'I' becomes the dotless i (U+0131). " +
            "End the argument with COLLATE \"C\" (PostgreSQL) or COLLATE Latin1_General_BIN2 (SQL Server), inside the call");
    }

    /// <summary>
    /// SQL Server compiles a batch up front, so migration 0050 runs the statement that reads a column it is about to drop
    /// as a string. The call inside that string is a call like any other; a scan that blanks string literals would lose it
    /// and the migration would pass for the wrong reason.
    /// </summary>
    [Fact]
    public void The_lower_call_a_migration_runs_as_dynamic_sql_is_found_and_names_its_collation()
    {
        var (_, _, sql) = MigrationFiles().Single(static file =>
            file.Name.Contains("0050_remove_authorization_configuration_key", StringComparison.Ordinal)
            && IsSqlServer(file.Provider));

        var call = SqlCaseFoldingScanner.Scan(sql, bracketIdentifiers: true).ShouldHaveSingleItem();

        call.InDynamicSql.ShouldBeTrue();
        call.NamesOrdinalCollation.ShouldBeTrue();

        // And it is the collation that makes it pass: without it the same migration is an offender.
        SqlCaseFoldingScanner
            .Scan(sql.Replace(" COLLATE Latin1_General_BIN2", string.Empty, StringComparison.Ordinal), bracketIdentifiers: true)
            .ShouldHaveSingleItem().NamesOrdinalCollation.ShouldBeFalse();
    }

    private static bool IsSqlServer(string provider) => string.Equals(provider, "SqlServer", StringComparison.Ordinal);

    private static IEnumerable<(string Provider, string Name, string Sql)> MigrationFiles()
    {
        foreach (var (provider, assembly) in new[]
        {
            ("PostgreSql", typeof(PostgresQueries).Assembly),
            ("SqlServer", typeof(SqlServerQueries).Assembly),
        })
        {
            foreach (var name in assembly
                .GetManifestResourceNames()
                .Where(static name => name.Contains(".Migrations", StringComparison.Ordinal))
                .Where(static name => name.EndsWith(".sql", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal))
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream, Encoding.UTF8);

                yield return (provider, name, reader.ReadToEnd());
            }
        }
    }
}
