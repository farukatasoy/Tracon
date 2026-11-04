using System.Text.RegularExpressions;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// A store runs a statement outside a transaction through <c>SqlStoreContext.ExecuteStatementAsync</c>, the one place a
/// statement the server picks as a deadlock victim is sent again.
/// </summary>
/// <remarks>
/// <para>
/// A text gate, like <see cref="SqlTransactionSiteTests"/>: a store that builds its own autocommit command passes every
/// contract test and fails only when a full run happens to race. A store call that is one statement (an event append, a
/// read, a delete) met a deadlock victim's error with the transaction of another call, and the caller got
/// <c>SqlException</c> 1205; the run event writer takes the first failed append for a store failure and drops the rest of
/// the run's events. <c>StatementDeadlockRetryTests</c> (SQL Server integration) proves the retry itself.
/// </para>
/// <para>
/// A command made on a connection and a transaction the caller holds (<c>CreateCommand(sql, connection, transaction)</c>)
/// is not an autocommit command and is not looked at here: it belongs to <c>ExecuteInTransactionAsync</c>, which
/// <see cref="SqlTransactionSiteTests"/> guards.
/// </para>
/// </remarks>
public sealed partial class SqlStatementSiteTests
{
    /// <summary>The files that may build an autocommit command themselves, and why.</summary>
    private static readonly Dictionary<string, string> Exceptions = new(StringComparer.Ordinal)
    {
        ["src/Tracon.Sql.Shared/Stores/SqlAuditLog.cs"] =
            "its commands run on the transaction a data subject erasure lends it, and its chain write sends itself again " +
            "after re-reading the last hash, which a plain repeat of the statement could not do",
        ["src/Tracon.PostgreSql/Stores/PgVectorSearchStore.cs"] =
            "reads and writes through its own NpgsqlDataSource, not a SqlStoreContext",
        ["src/Tracon.Sql.Shared/Stores/SqlDataSubjectStore.cs"] =
            "deletes content outside the database and calls back before its commit; running a statement of it again is not safe",
    };

    [Fact]
    public void A_store_sends_an_autocommit_statement_through_the_store_context()
    {
        var sites = new List<string>();

        foreach (var (relative, lines) in StoreFiles())
        {
            if (Exceptions.ContainsKey(relative))
            {
                continue;
            }

            sites.AddRange(AutocommitCommandSites(lines).Select(line => $"{relative}:{line}"));
        }

        sites.ShouldBeEmpty(
            "Run a statement outside a transaction with SqlStoreContext.ExecuteStatementAsync, where a deadlock victim " +
            "is sent again. A text of several statements whose earlier statements commit before a later one can fail " +
            "(a report that updates a row and then a counter) belongs in ExecuteInTransactionAsync. A file that cannot " +
            "use either needs an entry in Exceptions saying why.");
    }

    [Fact]
    public void Every_exception_still_builds_an_autocommit_command()
    {
        // A stale entry would silently exempt a file that was rewritten later.
        foreach (var relative in Exceptions.Keys)
        {
            var lines = File.ReadAllLines(Path.Combine(RepositoryRoot, relative));

            AutocommitCommandSites(lines).ShouldNotBeEmpty(relative);
        }
    }

    [Fact]
    public void Every_exemption_marker_has_a_reason_and_precedes_a_command()
    {
        var markers = new List<string>();

        foreach (var (relative, lines) in StoreFiles())
        {
            for (var index = 0; index < lines.Length; index++)
            {
                if (!lines[index].Contains("autocommit-exempt", StringComparison.Ordinal) || lines[index].TrimStart().StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }

                var next = index + 1 < lines.Length ? lines[index + 1] : string.Empty;

                if (!IsExemptionMarker(lines[index]) || !next.Contains("CreateCommand(", StringComparison.Ordinal))
                {
                    markers.Add($"{relative}:{index + 1}");
                }
            }
        }

        markers.ShouldBeEmpty("a marker needs a reason after the colon and sits on the line before the command it exempts");
    }

    [Fact]
    public void The_gate_sees_the_forms_a_store_builds_a_command_in()
    {
        string[] autocommit =
        [
            "        var command = CreateCommand(_sql.SelectRun);",
            "        var command = _context.CreateCommand(_sql.SelectRun);",
            "        var select = _context.CreateCommand(",
            "            _dialect.BuildRetentionSelectBatchSql(definition.Table, \"id\", where));",
        ];

        AutocommitCommandSites(autocommit).ShouldBe(new List<int> { 1, 2, 3 });

        string[] notAutocommit =
        [
            "        var command = _context.CreateCommand(_sql.UpsertTrace, connection, transaction);",
            "    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);",
            "        // var command = CreateCommand(_sql.SelectRun);",
            "        // autocommit-exempt: the rows are handed over one at a time and a repeat would hand them over twice",
            "        var streamed = CreateCommand(_sql.SelectRunEvents);",
            "    /// <see cref=\"SqlStoreContext.CreateCommand(string)\"/>",
            "        var insert = _context.CreateCommand(",
            "            _sql.InsertConversationItem,",
            "            connection,",
            "            transaction);",
        ];

        AutocommitCommandSites(notAutocommit).ShouldBeEmpty();
    }

    /// <summary>Finds the lines on which a store builds a command that is not on a connection and a transaction it was given.</summary>
    /// <param name="lines">The lines of one file.</param>
    /// <returns>The one-based line numbers.</returns>
    private static List<int> AutocommitCommandSites(string[] lines)
    {
        var found = new List<int>();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("//", StringComparison.Ordinal)
                || !line.Contains("CreateCommand(", StringComparison.Ordinal)
                || DefinitionOfCreateCommand().IsMatch(line)
                || (index > 0 && IsExemptionMarker(lines[index - 1])))
            {
                continue;
            }

            // The call may run over several lines: read up to the statement's end.
            var call = new System.Text.StringBuilder(line);

            for (var next = index + 1; next < lines.Length && !call.ToString().Contains(';', StringComparison.Ordinal); next++)
            {
                call.Append(' ').Append(lines[next].Trim());
            }

            var text = call.ToString();
            var afterOpen = text[(text.IndexOf("CreateCommand(", StringComparison.Ordinal) + "CreateCommand(".Length)..];

            if (afterOpen.Contains("connection", StringComparison.Ordinal) || afterOpen.Contains("lent.", StringComparison.Ordinal))
            {
                continue;
            }

            found.Add(index + 1);
        }

        return found;
    }

    /// <summary>
    /// Says whether a line is the marker that keeps one command out of the gate: <c>// autocommit-exempt:</c> and a reason of
    /// some length, on the line before the command.
    /// </summary>
    /// <param name="line">The line before the command.</param>
    /// <returns><see langword="true"/> when it is a marker with a reason.</returns>
    private static bool IsExemptionMarker(string line)
    {
        const string Marker = "// autocommit-exempt:";
        var trimmed = line.Trim();

        return trimmed.StartsWith(Marker, StringComparison.Ordinal) && trimmed.Length - Marker.Length >= 30;
    }

    private static IEnumerable<(string Relative, string[] Lines)> StoreFiles()
    {
        foreach (var project in (string[])["Tracon.Sql.Shared", "Tracon.PostgreSql", "Tracon.SqlServer", "Tracon.Sqlite"])
        {
            var stores = Path.Combine(RepositoryRoot, "src", project, "Stores");

            if (!Directory.Exists(stores))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(stores, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/');

                if (!relative.Contains("/obj/", StringComparison.Ordinal))
                {
                    yield return (relative, File.ReadAllLines(path));
                }
            }
        }
    }

    [GeneratedRegex(@"\bDbCommand\s+CreateCommand\(", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DefinitionOfCreateCommand();

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
