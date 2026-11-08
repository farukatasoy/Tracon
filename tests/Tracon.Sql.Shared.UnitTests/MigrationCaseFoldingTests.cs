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
/// <para>
/// A migration that was released is never edited, so four released files keep a call that follows the locale
/// (<see cref="FrozenFiles"/>). They are not a precedent: the list only shrinks, and a new migration names its
/// collation.
/// </para>
/// </remarks>
public sealed class MigrationCaseFoldingTests
{
    /// <summary>
    /// The released files whose <c>lower()</c> follows the locale of the database. Their bytes are frozen, and on a supported
    /// install each of them runs once, on a table that holds no row yet. The one case it can differ in is a database whose
    /// locale is Turkish and that already applied the file with its checksum patched by hand: the compatibility page names
    /// that limit. A file leaves the list when it is no longer an offender, and nothing joins it.
    /// </summary>
    private static readonly (string Provider, string Name, string Reason)[] FrozenFiles =
    [
        ("PostgreSql", "0038_provider_name_case", "published bytes are frozen; on a supported install the file runs on an empty table"),
        ("PostgreSql", "0062_remove_authorization_configuration_key", "published bytes are frozen; on a supported install the file runs on an empty table"),
        ("SqlServer", "0025_provider_name_case", "published bytes are frozen; on a supported install the file runs on an empty table"),
        ("SqlServer", "0050_remove_authorization_configuration_key", "published bytes are frozen; on a supported install the file runs on an empty table"),
    ];

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

                if (!call.NamesOrdinalCollation && !IsFrozen(provider, name))
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
    /// The list is a ceiling that goes down: at most four entries, each still a file that folds with the rules of the database.
    /// An entry whose file now names its collation (or is gone) is removed, and a new file that follows the locale is not
    /// added here: it is the offender the first test reports.
    /// </summary>
    [Fact]
    public void The_frozen_files_never_grow_and_each_still_follows_the_locale()
    {
        FrozenFiles.Length.ShouldBeLessThanOrEqualTo(4, "the list only shrinks");

        foreach (var (provider, name, reason) in FrozenFiles)
        {
            reason.ShouldNotBeNullOrWhiteSpace($"{provider}/{name}: an entry says why the file is frozen");

            var (_, _, sql) = MigrationFiles().Single(file => IsFile(file, provider, name));

            SqlCaseFoldingScanner.Scan(sql, bracketIdentifiers: IsSqlServer(provider))
                .Any(static call => !call.NamesOrdinalCollation)
                .ShouldBeTrue($"{provider}/{name} no longer follows the locale; remove it from the list");
        }
    }

    /// <summary>
    /// SQL Server compiles a batch up front, so migration 0050 runs the statement that reads a column it is about to drop
    /// as a string. The call inside that string is a call like any other; a scan that blanks string literals would lose it
    /// and the file would pass as if it held no call at all.
    /// </summary>
    [Fact]
    public void The_lower_call_a_migration_runs_as_dynamic_sql_is_found_and_follows_the_locale()
    {
        var (_, _, sql) = MigrationFiles().Single(static file => IsFile(file, "SqlServer", "0050_remove_authorization_configuration_key"));

        var call = SqlCaseFoldingScanner.Scan(sql, bracketIdentifiers: true).ShouldHaveSingleItem();

        call.InDynamicSql.ShouldBeTrue();
        call.NamesOrdinalCollation.ShouldBeFalse();

        // And it is the collation that makes it pass: with it, the same text is ordinal.
        SqlCaseFoldingScanner
            .Scan(
                sql.Replace("LOWER(existing_key.[key])", "LOWER(existing_key.[key] COLLATE Latin1_General_BIN2)", StringComparison.Ordinal),
                bracketIdentifiers: true)
            .ShouldHaveSingleItem().NamesOrdinalCollation.ShouldBeTrue();
    }

    private static bool IsSqlServer(string provider) => string.Equals(provider, "SqlServer", StringComparison.Ordinal);

    private static bool IsFrozen(string provider, string resourceName)
        => FrozenFiles.Any(file => string.Equals(file.Provider, provider, StringComparison.Ordinal)
            && resourceName.Contains(file.Name, StringComparison.Ordinal));

    private static bool IsFile((string Provider, string Name, string Sql) file, string provider, string name)
        => string.Equals(file.Provider, provider, StringComparison.Ordinal) && file.Name.Contains(name, StringComparison.Ordinal);

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
