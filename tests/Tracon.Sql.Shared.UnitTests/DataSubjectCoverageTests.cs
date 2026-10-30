using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// Every table that ties a row to a session, a run, or a conversation is
/// reached by data subject export and erasure — or carries a written reason why
/// it is not.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The same gap came back four times: conversations had no session id
///Attachment bytes stayed in storage, workflow checkpoints
/// had no foreign key to runs, and run scores were matched only by the
/// run ids a resolver named. Each time the table was simply not in
/// <c>DataSubjectTargetRegistry</c>, and nothing failed. This gate turns the
/// next one into a build failure.
/// </para>
/// <para>
/// A table is covered when it is an export target, or when a foreign key with
/// <c>ON DELETE CASCADE</c> ties it to an erasure target (the run's events, tool
/// calls, and traces go with the run; an earlier change left them out of the export on
/// purpose). Any other table needs an entry in <see cref="Exemptions"/>.
/// </para>
/// <para>
/// The schema is read from each provider's own embedded migrations, and the
/// registry through reflection: the registry is compiled into each provider
/// assembly, so a direct reference from this project would be ambiguous.
/// </para>
/// </remarks>
public sealed class DataSubjectCoverageTests
{
    /// <summary>The tables that tie a row to a subject but are neither a target nor cascaded, and why.</summary>
    private static readonly Dictionary<string, string> Exemptions = new(StringComparer.Ordinal)
    {
        ["eval_case_results"] =
            "run_id names the run an evaluation made for its own test case, not a data subject's run.",
    };

    /// <summary>A column that ties a row to a session, a run, or a conversation.</summary>
    private static readonly Regex SubjectColumn = new(
        @"^(?:session_id|run_id|conversation_id|\w+_session_id|\w+_run_id|\w+_conversation_id)$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    private static readonly Regex CreateTable = new(
        @"CREATE TABLE (?:IF NOT EXISTS )?\{schema\}\.?(?<table>\w+)\s*\((?<body>.*?)\n\);",
        RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    private static readonly Regex AlterTable = new(
        @"ALTER TABLE \{schema\}\.?(?<table>\w+)(?<body>.*?);",
        RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    private static readonly Regex AddedColumn = new(
        @"\bADD\s+(?:COLUMN\s+)?(?:IF NOT EXISTS\s+)?(?<column>\w+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    private static readonly Regex ColumnLine = new(
        @"^\s*(?<column>\w+)\s+\w+",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    private static readonly string[] NotColumns = ["CONSTRAINT", "PRIMARY", "UNIQUE", "FOREIGN", "CHECK", "INDEX"];

    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public void Every_table_tied_to_a_subject_is_reached_or_exempted_with_a_reason(string provider)
    {
        var assembly = ProviderAssembly(provider);
        var schema = ReadSchema(EmbeddedMigrations(assembly));
        var exportTargets = RegistryList(assembly, "ExportTargets");
        var eraseTargets = RegistryList(assembly, "EraseTargets");

        var cascadeParents = string.Join("|", eraseTargets.Select(Regex.Escape));
        var cascade = new Regex(
            $@"REFERENCES \{{schema\}}\.?(?:{cascadeParents})\s*\(\s*id\s*\)\s*ON DELETE CASCADE",
            RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
            TimeSpan.FromSeconds(1));

        var missing = new List<string>();

        foreach (var (table, (columns, body)) in schema.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            var subjectColumns = columns.Where(column => SubjectColumn.IsMatch(column)).ToList();

            if (subjectColumns.Count == 0
                || exportTargets.Contains(table, StringComparer.Ordinal)
                || cascade.IsMatch(body)
                || Exemptions.ContainsKey(table))
            {
                continue;
            }

            missing.Add($"{table} ({string.Join(", ", subjectColumns)})");
        }

        missing.ShouldBeEmpty(
            "A table that ties a row to a session, a run, or a conversation is skipped by data subject export " +
            "and erasure in silence. Add a target to DataSubjectTargetRegistry, or an entry with a reason to " +
            "DataSubjectCoverageTests.Exemptions.");

        exportTargets.Where(target => !schema.ContainsKey(target)).ShouldBeEmpty(
            "An export target names a table that the migrations do not create.");
        Exemptions.Keys.Where(table => !schema.ContainsKey(table)).ShouldBeEmpty(
            "An exemption names a table that the migrations do not create.");
    }

    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public void A_target_whose_condition_reads_runs_is_erased_before_runs(string provider)
    {
        // The erasure runs every DELETE in one transaction, in EraseTargets
        // order. A condition that finds rows through the runs table finds
        // nothing once the runs are gone: run_scores matched by session was
        // erased AFTER runs and kept every score.
        var assembly = ProviderAssembly(provider);
        var registry = Registry(assembly);
        var dialect = Dialect(provider);

        try
        {
            var eraseTargets = RegistryList(assembly, "EraseTargets");
            var resolve = registry.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static)!;

            var runsTable = Table(resolve, dialect, "runs");
            var runsPosition = eraseTargets.ToList().IndexOf("runs");
            runsPosition.ShouldBeGreaterThanOrEqualTo(0);

            var late = eraseTargets
                .Skip(runsPosition + 1)
                .Where(target => Predicate(resolve, dialect, target).Contains($"FROM {runsTable}", StringComparison.Ordinal))
                .ToList();

            late.ShouldBeEmpty("A target whose condition reads the runs table is erased after the runs are deleted.");
        }
        finally
        {
            (dialect as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Where each <see cref="ProtectedColumn"/> lives. A new member fails the test
    /// below until it is placed here, so its export is decided rather than forgotten.
    /// </summary>
    private static readonly Dictionary<ProtectedColumn, (string Table, string Column)> ProtectedColumnHomes = new()
    {
        [ProtectedColumn.SessionState] = ("sessions", "state"),
        [ProtectedColumn.ConversationItem] = ("conversation_items", "item"),
        [ProtectedColumn.RunInput] = ("run_inputs", "messages"),
        [ProtectedColumn.RunInputParameters] = ("run_inputs", "parameters"),
        [ProtectedColumn.RunEventText] = ("run_events", "text"),
        [ProtectedColumn.RunEventPayload] = ("run_events", "payload"),
        [ProtectedColumn.ToolArguments] = ("tool_invocations", "arguments"),
        [ProtectedColumn.ToolResult] = ("tool_invocations", "result"),
        [ProtectedColumn.AgentFileContent] = ("agent_files", "content"),
        [ProtectedColumn.AttachmentContent] = ("attachments", "content"),
        [ProtectedColumn.ResponsePayload] = ("responses", "payload"),
        [ProtectedColumn.WorkflowCheckpointState] = ("workflow_checkpoints", "state"),
    };

    /// <summary>
    /// Export wrote a protected column as its stored envelope, so a subject
    /// received ciphertext exactly when content protection was on. Every protected
    /// column of an export target is read back as plaintext.
    /// </summary>
    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public void Every_protected_column_of_an_export_target_is_exported_as_plaintext(string provider)
    {
        Enum.GetValues<ProtectedColumn>().Where(column => !ProtectedColumnHomes.ContainsKey(column)).ShouldBeEmpty(
            "A ProtectedColumn has no table here: place it, then decide whether export reads it back.");

        var assembly = ProviderAssembly(provider);
        var exportTargets = RegistryList(assembly, "ExportTargets");
        var protectedColumnsOf = Registry(assembly).GetMethod("ProtectedExportColumns", BindingFlags.Public | BindingFlags.Static)!;

        var missing = ProtectedColumnHomes.Values
            .Where(home => exportTargets.Contains(home.Table, StringComparer.Ordinal))

            // An attachment's bytes are left out of the export on purpose (DataSubjectExport).
            .Where(static home => home != ("attachments", "content"))
            .Where(home => !((IReadOnlyList<string>)protectedColumnsOf.Invoke(null, [home.Table])!).Contains(home.Column, StringComparer.Ordinal))
            .Select(static home => $"{home.Table}.{home.Column}")
            .ToList();

        missing.ShouldBeEmpty(
            "An exported column that content protection can encrypt is not in " +
            "DataSubjectTargetRegistry.ProtectedExportColumns: the subject would receive ciphertext.");
    }

    /// <summary>
    /// A retention archive hands rows to the consumer's sink. Every protected
    /// column of a retention target is read back as plaintext there too.
    /// </summary>
    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public void Every_protected_column_of_a_retention_target_is_archived_as_plaintext(string provider)
    {
        var protectedColumnsOf = ProviderAssembly(provider)
            .GetType("Tracon.RetentionTargetRegistry", throwOnError: true)!
            .GetMethod("ProtectedArchiveColumns", BindingFlags.Public | BindingFlags.Static)!;

        var missing = ProtectedColumnHomes.Values
            .Where(static home => RetentionTargets.All.Contains(home.Table, StringComparer.Ordinal))
            .Where(home => !((IReadOnlyList<string>)protectedColumnsOf.Invoke(null, [home.Table])!).Contains(home.Column, StringComparer.Ordinal))
            .Select(static home => $"{home.Table}.{home.Column}")
            .ToList();

        missing.ShouldBeEmpty(
            "A retention target column that content protection can encrypt is not in " +
            "RetentionTargetRegistry.ProtectedArchiveColumns: the archive sink would receive ciphertext.");
    }

    private static Assembly ProviderAssembly(string provider) => provider switch
    {
        "PostgreSql" => typeof(PostgresQueries).Assembly,
        "SqlServer" => typeof(SqlServerQueries).Assembly,
        _ => typeof(SqliteQueries).Assembly,
    };

    private static object Dialect(string provider) => provider switch
    {
        "PostgreSql" => new PostgresDialect("tracon"),
        "SqlServer" => new SqlServerDialect("tracon"),
        _ => new SqliteDialect("tracon_"),
    };

    private static Type Registry(Assembly assembly)
        => assembly.GetType("Tracon.DataSubjectTargetRegistry", throwOnError: true)!;

    private static IReadOnlyList<string> RegistryList(Assembly assembly, string property)
        => (IReadOnlyList<string>)Registry(assembly)
            .GetProperty(property, BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

    private static string Table(MethodInfo resolve, object dialect, string target)
        => (((string, string, string))resolve.Invoke(null, [dialect, target])!).Item1;

    private static string Predicate(MethodInfo resolve, object dialect, string target)
        => (((string, string, string))resolve.Invoke(null, [dialect, target])!).Item2;

    /// <summary>Every table the migrations create, with its columns and its definition text.</summary>
    private static Dictionary<string, (List<string> Columns, string Body)> ReadSchema(string migrations)
    {
        var sql = string.Join(
            '\n',
            migrations.Split('\n').Select(static line => line.Split("--", 2)[0]));

        var schema = new Dictionary<string, (List<string> Columns, string Body)>(StringComparer.Ordinal);

        foreach (Match match in CreateTable.Matches(sql))
        {
            var columns = match.Groups["body"].Value
                .Split('\n')
                .Select(static line => ColumnLine.Match(line))
                .Where(static column => column.Success)
                .Select(static column => column.Groups["column"].Value)
                .Where(static column => !NotColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
                .ToList();

            schema[match.Groups["table"].Value] = (columns, match.Groups["body"].Value);
        }

        foreach (Match match in AlterTable.Matches(sql))
        {
            if (!schema.TryGetValue(match.Groups["table"].Value, out var table))
            {
                continue;
            }

            table.Columns.AddRange(AddedColumn.Matches(match.Groups["body"].Value)
                .Select(static added => added.Groups["column"].Value)
                .Where(static column => !NotColumns.Contains(column, StringComparer.OrdinalIgnoreCase)));
        }

        return schema;
    }

    private static string EmbeddedMigrations(Assembly assembly)
    {
        var builder = new StringBuilder();

        // The mandatory set only, the same as MigrationParityTests: the optional
        // sets (pgvector, read views) are not applied to every deployment.
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
