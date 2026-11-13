using System.Reflection;
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
        ["src/Tracon.PostgreSql/Stores/PgVectorSearchStore.cs"] =
            "reads and writes through its own NpgsqlDataSource, not a SqlStoreContext",
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
    public void The_tenant_free_api_key_hash_query_is_read_by_the_authentication_lookup_alone()
    {
        // SelectApiKeyByHash has no tenant filter on purpose: the tenant is its output. A store call that already knows
        // the tenant (the read-back of a key it has just written) takes the row by its id and tenant instead, so that
        // the one query that cannot enforce a tenant has exactly one caller, whose job it is.
        var sites = new List<string>();

        foreach (var (relative, lines) in StoreFiles())
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("SelectApiKeyByHash", StringComparison.Ordinal))
                {
                    sites.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        sites.Count.ShouldBe(
            1,
            $"SelectApiKeyByHash is the authentication lookup's query alone; its callers:{Environment.NewLine}{string.Join(Environment.NewLine, sites)}");
        sites[0].ShouldStartWith("src/Tracon.Sql.Shared/Stores/SqlApiKeyStore.cs:", Case.Sensitive);
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
    public void A_text_that_writes_in_more_than_one_statement_does_not_go_through_the_statement_boundary()
    {
        // Sending a victim's statement again is safe because the server rolled it back whole. A text of several statements
        // that write is not one unit in autocommit: the earlier ones are committed when a later one is the victim, and
        // running the text again repeats what is done (the report of a job item counted nothing the second time). Such a
        // text belongs in ExecuteInTransactionAsync. A write to a table variable lasts as long as the batch and does not count.
        var queries = new SqlServerQueries("tracon");
        var texts = queries.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0)
            .ToDictionary(property => property.Name, property => (string?)property.GetValue(queries) ?? string.Empty, StringComparer.Ordinal);
        var offenders = new List<string>();
        var seen = 0;

        foreach (var (relative, lines) in StoreFiles())
        {
            var source = string.Join('\n', lines);

            for (var at = source.IndexOf("ExecuteStatementAsync(", StringComparison.Ordinal);
                at >= 0;
                at = source.IndexOf("ExecuteStatementAsync(", at + 1, StringComparison.Ordinal))
            {
                var window = source.Substring(at, Math.Min(400, source.Length - at));

                foreach (Match name in QueryNameInCall().Matches(window))
                {
                    if (!texts.TryGetValue(name.Groups["name"].Value, out var text))
                    {
                        continue;
                    }

                    seen++;

                    if (PersistentWrites(text) > 1)
                    {
                        offenders.Add($"{relative}: {name.Groups["name"].Value}");
                    }
                }
            }
        }

        seen.ShouldBeGreaterThan(150, "the scan has to find the statement sites it guards");
        offenders.Distinct(StringComparer.Ordinal).ShouldBeEmpty(
            "a text that writes in more than one statement is sent through ExecuteInTransactionAsync, not ExecuteStatementAsync");
    }

    [Fact]
    public void The_write_count_sees_the_text_of_a_job_item_report_and_not_a_table_variable()
    {
        var queries = new SqlServerQueries("tracon");

        PersistentWrites(queries.ReportJobItem).ShouldBe(2, "the report writes the item row and the counters of the job");
        PersistentWrites(queries.LeaseJobWithTenantCap).ShouldBe(1, "only the final UPDATE is durable; the INSERT goes into a table variable");
        PersistentWrites(queries.SelectRunStatistics).ShouldBe(0);
    }

    private static int PersistentWrites(string text)
    {
        var withoutComments = LineComment().Replace(text, string.Empty);

        return withoutComments
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Count(static statement => DurableWrite().IsMatch(statement));
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

            // A word in a comment is not an argument: these were taken for a command on a held connection.
            "        var commented = _context.CreateCommand(_sql.SelectRun); // not on a held connection",
            "        var split = _context.CreateCommand(",
            "            _sql.SelectRun); // the context's own connection",
            "        var named = _context.CreateCommand(_sql.SelectConnectionStats);",
            "        var elsewhere = _context.CreateCommand(_sql.SelectRun, other, lent);",
        ];

        AutocommitCommandSites(autocommit).ShouldBe(new List<int> { 1, 2, 3, 5, 6, 8, 9 });

        string[] notAutocommit =
        [
            "        var command = _context.CreateCommand(_sql.UpsertTrace, connection, transaction);",
            "            ? _context.CreateCommand(sql, lent.Connection, lent.Transaction)",
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

            // The call may run over several lines: read up to the statement's end. A comment is not part of the call.
            var call = new System.Text.StringBuilder(WithoutLineComment(line));

            for (var next = index + 1; next < lines.Length && !call.ToString().Contains(';', StringComparison.Ordinal); next++)
            {
                call.Append(' ').Append(WithoutLineComment(lines[next]).Trim());
            }

            var text = call.ToString();
            var arguments = CallArguments(text, text.IndexOf("CreateCommand(", StringComparison.Ordinal) + "CreateCommand(".Length);

            // On a connection and a transaction the caller holds: the second argument is the connection itself.
            if (arguments.Count >= 3 && arguments[1] is "connection" or "lent.Connection")
            {
                continue;
            }

            found.Add(index + 1);
        }

        return found;
    }

    /// <summary>Cuts the comment that ends a line, leaving a <c>//</c> inside a string literal alone.</summary>
    /// <param name="line">One line of source.</param>
    /// <returns>The line without its trailing comment.</returns>
    private static string WithoutLineComment(string line)
    {
        var inString = false;

        for (var at = 0; at < line.Length; at++)
        {
            if (line[at] == '"' && (at == 0 || line[at - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (!inString && line[at] == '/' && at + 1 < line.Length && line[at + 1] == '/')
            {
                return line[..at];
            }
        }

        return line;
    }

    /// <summary>Splits the arguments of a call at the commas that are not inside a nested call, a string or a bracket.</summary>
    /// <param name="text">The statement, comments removed.</param>
    /// <param name="start">The index just after the opening parenthesis.</param>
    /// <returns>The trimmed arguments, up to the closing parenthesis.</returns>
    private static List<string> CallArguments(string text, int start)
    {
        var arguments = new List<string>();
        var current = new System.Text.StringBuilder();
        var depth = 0;
        var inString = false;

        for (var at = start; at < text.Length; at++)
        {
            var character = text[at];

            if (character == '"' && (at == 0 || text[at - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (!inString)
            {
                if (character is '(' or '[' or '{')
                {
                    depth++;
                }
                else if (character is ')' or ']' or '}')
                {
                    if (depth == 0)
                    {
                        break;
                    }

                    depth--;
                }
                else if (character == ',' && depth == 0)
                {
                    arguments.Add(current.ToString().Trim());
                    current.Clear();

                    continue;
                }
            }

            current.Append(character);
        }

        arguments.Add(current.ToString().Trim());

        return arguments;
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

    // The name of a query in the first arguments of a call: _sql.SelectRun, _context.Sql.DeleteAttachment.
    [GeneratedRegex(@"(?:_sql|\.Sql)\.(?<name>[A-Za-z]+)\b", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex QueryNameInCall();

    [GeneratedRegex(@"--[^\r\n]*", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LineComment();

    // A statement that changes a table that outlives the batch: INSERT INTO, UPDATE or DELETE whose target is not a variable.
    [GeneratedRegex(@"\b(?:INSERT\s+INTO\s+[^@\s]|UPDATE\s+[^@\s(]|DELETE\s+(?:FROM\s+)?[^@\s])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DurableWrite();

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
