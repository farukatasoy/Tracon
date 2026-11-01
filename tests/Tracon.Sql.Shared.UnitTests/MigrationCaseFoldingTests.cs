using System.Text;
using System.Text.RegularExpressions;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// A migration that lowers or uppers a value says which rules it means.
/// </summary>
/// <remarks>
/// <para>
/// <c>lower('I')</c> is <c>i</c> in most databases and <c>ı</c> (dotless) in a database whose locale is Turkish.
/// A migration that rewrites a value with it, or looks one up with it, then does something else on a Turkish
/// database than anywhere else: <c>OpenAI</c> became <c>openaı</c>, and <c>AUTHORIZATION</c> was no longer found by
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
    private static readonly Regex FoldingCall = new(@"\b(?:lower|upper)\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    private static readonly Regex OrdinalCollation = new(@"\bCOLLATE\s+(?:""C""|""POSIX""|\w+_BIN2?\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    [Fact]
    public void Every_lower_and_upper_in_a_migration_names_an_ordinal_collation()
    {
        var offenders = new List<string>();
        var seen = 0;

        foreach (var (provider, name, sql) in MigrationFiles())
        {
            foreach (var call in FoldingCalls(StripComments(sql)))
            {
                seen++;

                if (!OrdinalCollation.IsMatch(call))
                {
                    offenders.Add($"{provider}/{name}: {call}");
                }
            }
        }

        seen.ShouldBeGreaterThan(0, "the scan found no lower() or upper() at all, so it would pass on nothing");

        offenders.ShouldBeEmpty(
            "lower() and upper() follow the locale of the database: on a Turkish database 'I' becomes 'ı'. " +
            "Write COLLATE \"C\" (PostgreSQL) or COLLATE Latin1_General_BIN2 (SQL Server) inside the call");
    }

    [Theory]
    [InlineData("SELECT lower(a COLLATE \"C\") FROM t", 0)]
    [InlineData("SELECT lower(a) FROM t", 1)]
    [InlineData("WHERE LOWER(x.[key] COLLATE Latin1_General_BIN2) = N'a'", 0)]
    [InlineData("WHERE UPPER(f(x)) = 'A'", 1)]
    [InlineData("-- lower(a) would be flagged\nSELECT 1", 0)]
    public void The_scan_tells_a_collated_call_from_a_bare_one(string sql, int bare)
        => FoldingCalls(StripComments(sql)).Count(static call => !OrdinalCollation.IsMatch(call)).ShouldBe(bare);

    /// <summary>Each call with its whole argument list, nested parentheses included.</summary>
    private static IEnumerable<string> FoldingCalls(string sql)
    {
        foreach (Match match in FoldingCall.Matches(sql))
        {
            var depth = 0;
            var end = match.Index + match.Length - 1;

            for (; end < sql.Length; end++)
            {
                if (sql[end] == '(')
                {
                    depth++;
                }
                else if (sql[end] == ')' && --depth == 0)
                {
                    break;
                }
            }

            yield return sql[match.Index..Math.Min(end + 1, sql.Length)];
        }
    }

    private static string StripComments(string sql)
        => Regex.Replace(sql, @"--[^\r\n]*", string.Empty, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

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
