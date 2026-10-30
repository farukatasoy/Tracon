using System.Reflection;
using System.Text.RegularExpressions;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// Every two-statement SQL Server upsert runs inside a transaction and seeks its
/// key.
/// </summary>
/// <remarks>
/// <para>
/// The SQL Server upsert is <c>UPDATE … WITH (UPDLOCK, SERIALIZABLE)</c> followed by
/// <c>IF @@ROWCOUNT = 0 INSERT</c>. Two conditions make it atomic, and the
/// contract tests see neither until a full-solution run happens to race:
/// </para>
/// <list type="number">
///   <item><description>
///   A transaction around both statements. In autocommit the range lock ends with
///   the UPDATE and two sessions that write a new key both insert. 20 of the 28
///   upserts ran that way; every one of them threw a unique-key violation under
///   <c>UpsertRaceTests</c>.
///   </description></item>
///   <item><description>
///   A WHERE that seeks. A wrapped key column (<c>ISNULL(col, N'') = …</c>) makes
///   the UPDATE read the neighbouring keys; inside the transaction the two sessions
///   then deadlock on each other's INSERT (measured on <c>skill_script_grants</c>).
///   </description></item>
/// </list>
/// <para>
/// This is a text gate, like <c>TenantParameterChokePointTests</c>: a new upsert
/// passes every contract test while it is still racy, so the rule is checked where
/// it is written. <c>UpsertRaceTests</c> (SQL Server integration) proves the
/// behaviour for one store of each shape.
/// </para>
/// </remarks>
public sealed partial class SqlServerUpsertAtomicityTests
{
    [Fact]
    public void Every_two_statement_upsert_runs_inside_a_transaction()
    {
        var upserts = TwoStatementUpserts();
        upserts.Count.ShouldBeGreaterThan(20, "the scan found the upserts it is meant to guard");

        var sources = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src", "Tracon.Sql.Shared"), "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.EndsWith("Queries.cs", StringComparison.Ordinal))
            .Select(static path => (Path: path, Lines: File.ReadAllLines(path)))
            .ToList();

        var problems = new List<string>();

        foreach (var name in upserts.Keys)
        {
            var reference = new Regex($@"\.{name}\b", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(5));
            var uses = 0;

            foreach (var (path, lines) in sources)
            {
                for (var index = 0; index < lines.Length; index++)
                {
                    if (!reference.IsMatch(lines[index]) || lines[index].TrimStart().StartsWith("///", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    uses++;

                    if (!RunsInsideATransaction(lines, index))
                    {
                        problems.Add($"{Path.GetRelativePath(RepositoryRoot, path)}:{index + 1} runs {name} outside a transaction");
                    }
                }
            }

            if (uses == 0)
            {
                problems.Add($"{name} is never run from src/Tracon.Sql.Shared; the gate cannot see where it executes");
            }
        }

        problems.ShouldBeEmpty(
            "Run a two-statement upsert through SqlStoreContext.ExecuteUpsertAsync, or on a command " +
            "created with an explicit transaction.");
    }

    [Fact]
    public void No_two_statement_upsert_wraps_a_key_column()
    {
        var wrapped = TwoStatementUpserts()
            .Where(static pair => WrappedColumnComparison().IsMatch(pair.Value))
            .Select(static pair => pair.Key)
            .ToList();

        wrapped.ShouldBeEmpty(
            "Compare a nullable key column with EXISTS (SELECT col INTERSECT SELECT @p), " +
            "or through a persisted computed column; a wrapped column cannot seek.");
    }

    private static bool RunsInsideATransaction(string[] lines, int index)
    {
        if (lines[index].Contains("ExecuteUpsertAsync(", StringComparison.Ordinal)
            || lines[index].Contains("transaction)", StringComparison.Ordinal))
        {
            return true;
        }

        for (var previous = index - 1; previous >= 0; previous--)
        {
            if (lines[previous].Trim().Length > 0)
            {
                return lines[previous].TrimEnd().EndsWith("ExecuteUpsertAsync(", StringComparison.Ordinal);
            }
        }

        return false;
    }

    private static Dictionary<string, string> TwoStatementUpserts()
    {
        var queries = new SqlServerQueries("tracon");

        return queries.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0)
            .Select(property => (property.Name, Text: (string?)property.GetValue(queries) ?? string.Empty))
            .Where(static query => TwoStatementUpsert().IsMatch(query.Text))
            .ToDictionary(static query => query.Name, static query => query.Text, StringComparer.Ordinal);
    }

    // The second statement of the upsert: the INSERT branch guarded by the first.
    [GeneratedRegex(
        @"IF\s+(?:@@ROWCOUNT\s*=\s*0|@updated\s*=\s*0|NOT\s+EXISTS\s*\(\s*SELECT\s+1\s+FROM\s+tracon\.)[\s\S]*?\bINSERT\b",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 5000)]
    private static partial Regex TwoStatementUpsert();

    // A column (never a parameter) wrapped in ISNULL on the left of an equality.
    [GeneratedRegex(
        @"ISNULL\(\s*[a-z_]+\s*,[^)]*\)\s*=",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 5000)]
    private static partial Regex WrappedColumnComparison();

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}' for Tracon.slnx.");
    }
}
