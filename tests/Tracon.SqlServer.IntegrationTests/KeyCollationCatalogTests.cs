using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// Reads the migrated schema and holds every text column of a primary key, a unique constraint or a unique
/// index to the ordinal comparison, unless this file names the column and says why it may differ.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 SQL Server compares text by the collation of the database, and its default ignores letter case. A key
/// the client chooses then folds two ids that differ in case into one row, which PostgreSQL and SQLite keep
/// apart. A migration that adds such a column, with no <c>COLLATE</c>, brings the defect back and nothing
/// else fails.
/// </para>
/// <para>
/// The check reads the catalog, not the migration text: the migrations are cumulative, and only the catalog
/// holds the shape they add up to. A column in the list that no longer is a key, or already compares
/// ordinally, is an error too, so the list cannot go stale.
/// </para>
/// </remarks>
public sealed class KeyCollationCatalogTests(SqlServerFixture fixture)
{
    private const string Ordinal = "Latin1_General_BIN2";

    private const string Folded = "folded to lower case before it is stored, so no two spellings exist";

    private const string Digest = "a digest the library computes, always in the one case it writes";

    private const string LibraryName = "a name the library chooses in its own code";

    private const string TraceId = "the trace id the runtime assigns, always in the lower-case hex it writes";

    /// <summary>
    /// The key columns that may keep the collation of the database, each with the reason. Tenant ids are matched
    /// by column name in every table (<see cref="IsTenantId"/>).
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        ["tenants.slug"] = Folded,
        ["tenant_provider_bindings.provider_name"] = Folded,
        ["audit_log.prev_hash"] = Digest,
        ["traces.trace_id"] = TraceId,
        ["tool_approval_rules.arguments_hash"] = Digest,
        ["tool_approval_rules.conditions_hash"] = Digest,
        ["tool_approval_rules.source_fingerprint"] = Digest,
        ["singleton_leases.name"] = LibraryName,
        ["retention_policies.target"] = LibraryName,
    };

    /// <summary>The tables whose <c>name</c> column is the name of a definition a client chooses.</summary>
    private static readonly string[] DefinitionTables =
    [
        "agent_definitions", "agent_definition_version_counters", "workflows", "workflow_definition_versions", "agent_skills",
        "agent_skill_resources", "agent_skill_scripts", "eval_suites", "experiments", "inbound_triggers", "job_schedules",
        "mcp_servers", "webhook_subscriptions",
    ];

    [Fact]
    public async Task Every_text_key_column_compares_ordinally_or_is_listed_with_its_reason()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        var keys = await TextKeyColumnsAsync(context);

        keys.ShouldNotBeEmpty("the catalog query found no key at all, which would make this check pass on nothing");
        keys.Count(static key => string.Equals(key.Collation, Ordinal, StringComparison.Ordinal))
            .ShouldBeGreaterThan(0, "the migration that makes the client's keys ordinal is part of the set");

        var unlisted = keys
            .Where(static key => !string.Equals(key.Collation, Ordinal, StringComparison.Ordinal))
            .Where(static key => !IsTenantId(key) && !Allowed.ContainsKey(key.Name))
            .Select(static key => $"{key.Name} ({key.Collation})")
            .Order(StringComparer.Ordinal)
            .ToList();

        unlisted.ShouldBeEmpty(
            "a text key column with no COLLATE Latin1_General_BIN2 folds two ids that differ in case into one row; " +
            "give the column that collation in its migration, or name it in Allowed with the reason it may differ");
    }

    [Fact]
    public async Task No_entry_of_the_list_is_stale()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        var keys = await TextKeyColumnsAsync(context);

        var stale = Allowed.Keys
            .Where(entry => !keys.Any(key => string.Equals(key.Name, entry, StringComparison.Ordinal)
                && !string.Equals(key.Collation, Ordinal, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToList();

        stale.ShouldBeEmpty(
            "a listed column that is no longer a key, or already compares ordinally, leaves the list; " +
            "otherwise the list stops meaning anything");
    }

    [Fact]
    public async Task Every_column_that_carries_a_session_id_compares_ordinally()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        // SQL Server refuses to compare two columns of different collations, and a lookup by the one left
        // case-insensitive lists the runs of two sessions as one. The identity has one collation everywhere.
        var columns = await ColumnsAsync(
            context,
            "(c.name = N'session_id' OR (t.name = N'sessions' AND c.name = N'id')) AND c.collation_name IS NOT NULL");

        columns.Count.ShouldBeGreaterThan(5, "the query has to find the session id columns it guards");
        columns
            .Where(static column => !string.Equals(column.Collation, Ordinal, StringComparison.Ordinal))
            .Select(static column => $"{column.Name} ({column.Collation})")
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty("one identity, one collation: every column of a session id compares ordinally");
    }

    [Fact]
    public async Task Every_column_that_names_a_request_or_a_parent_or_a_checkpoint_compares_ordinally()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        // Those columns carry an id that another column carries too (the parent of a checkpoint is the checkpoint_id of an
        // earlier row), and no index makes them a key, so the test above never meets them. A comparison of the two stops with
        // "Cannot resolve the collation conflict" the day somebody writes one.
        var columns = await ColumnsAsync(
            context,
            "c.name IN (N'request_id', N'parent_id', N'checkpoint_id') AND c.collation_name IS NOT NULL");

        columns.Count.ShouldBeGreaterThan(2, "the query has to find the columns it guards");
        columns
            .Where(static column => !string.Equals(column.Collation, Ordinal, StringComparison.Ordinal))
            .Select(static column => $"{column.Name} ({column.Collation})")
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty("one identity, one collation: every column of a request, parent or checkpoint id compares ordinally");
    }

    [Fact]
    public async Task Every_column_that_carries_a_definition_name_compares_ordinally()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        // The name of an agent lives in its definition and again in every table that records what ran under it. A column of
        // another collation makes two definitions that differ only in letter case one in a filter or a group, and stops a
        // comparison with the definition's own column ("Cannot resolve the collation conflict"). The pattern finds a column a
        // later migration adds under one of these names, so the family cannot grow a member the check does not see.
        var columns = await ColumnsAsync(
            context,
            "c.collation_name IS NOT NULL AND (c.name IN (N'agent_name', N'workflow_name', N'skill_name', N'script_name', N'target_name', N'tool_name') " +
            "OR (c.name = N'name' AND t.name IN (" + string.Join(", ", DefinitionTables.Select(static table => $"N'{table}'")) + ")))");

        columns.Count.ShouldBeGreaterThan(30, "the query has to find the name columns it guards");
        columns
            .Where(static column => !string.Equals(column.Collation, Ordinal, StringComparison.Ordinal))
            .Select(static column => $"{column.Name} ({column.Collation})")
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty("one identity, one collation: every column that holds the name of a definition compares ordinally");
    }

    [Fact]
    public async Task The_label_columns_that_stay_out_of_the_family_are_not_taken_for_names()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        // A node label, a span name, a score name and a key label are written by the library or by the model and never compared
        // with the name of a definition. The list says which columns the test above does NOT guard, so the next reader does not
        // add them to the family by accident, and a rename that moves one of them into the guarded set shows here first.
        var labels = await ColumnsAsync(
            context,
            "c.collation_name IS NOT NULL AND ((t.name = N'run_non_token_usage' AND c.name = N'node_name') " +
            "OR (t.name = N'spans' AND c.name = N'name') OR (t.name = N'api_keys' AND c.name = N'name'))");

        labels.Count.ShouldBe(3, "a label column was renamed or moved: decide again whether it belongs to the family");
    }

    private static bool IsTenantId(KeyColumn key)
        => key.Name.EndsWith(".tenant_id", StringComparison.Ordinal);

    private static ValueTask<IReadOnlyList<KeyColumn>> TextKeyColumnsAsync(SqlServerTestContext context)
        => ColumnsAsync(
            context,
            "c.collation_name IS NOT NULL AND EXISTS (" +
            "SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id " +
            "WHERE i.object_id = c.object_id AND ic.column_id = c.column_id AND ic.is_included_column = 0 " +
            "AND (i.is_primary_key = 1 OR i.is_unique = 1 OR i.is_unique_constraint = 1))");

    /// <summary>The text columns of the schema that satisfy <paramref name="predicate"/>, as <c>table.column</c> and collation.</summary>
    private static async ValueTask<IReadOnlyList<KeyColumn>> ColumnsAsync(SqlServerTestContext context, string predicate)
    {
        var rows = await context.CatalogScalarAsync<string>(
            "SELECT STRING_AGG(CAST(t.name + N'.' + c.name + N' ' + c.collation_name AS nvarchar(max)), N';') " +
            "FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id " +
            $"WHERE SCHEMA_NAME(t.schema_id) = N'{context.SchemaName}' AND t.name <> N'__migrations' AND c.collation_name IS NOT NULL AND ({predicate});");

        return (rows ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(static entry => entry.Split(' ', 2))
            .Select(static parts => new KeyColumn(parts[0], parts[1]))
            .ToList();
    }

    private sealed record KeyColumn(string Name, string Collation);
}
