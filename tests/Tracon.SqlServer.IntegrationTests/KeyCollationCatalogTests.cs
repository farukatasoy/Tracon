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

    private const string DefinitionName = "the name of a definition, which the database collation still folds";

    /// <summary>
    /// The key columns that may keep the collation of the database, each with the reason. Tenant ids are matched
    /// by column name in every table (<see cref="IsTenantId"/>).
    /// </summary>
    /// <remarks>
    /// The <see cref="DefinitionName"/> entries are a known gap, not a verdict: an agent called <c>Support</c> and
    /// one called <c>support</c> are one definition on SQL Server and two elsewhere. Changing them drags every
    /// column that repeats the name (runs, sessions, quotas) with it, so they leave in a change of their own.
    /// </remarks>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        ["tenants.slug"] = Folded,
        ["tenant_provider_bindings.provider_name"] = Folded,
        ["audit_log.prev_hash"] = Digest,
        ["traces.trace_id"] = Digest,
        ["tool_approval_rules.arguments_hash"] = Digest,
        ["tool_approval_rules.conditions_hash"] = Digest,
        ["tool_approval_rules.source_fingerprint"] = Digest,
        ["singleton_leases.name"] = LibraryName,
        ["retention_policies.target"] = LibraryName,
        ["agent_definition_version_counters.name"] = DefinitionName,
        ["agent_definitions.name"] = DefinitionName,
        ["agent_files.agent_name"] = DefinitionName,
        ["agent_skill_resources.name"] = DefinitionName,
        ["agent_skill_scripts.name"] = DefinitionName,
        ["agent_skills.name"] = DefinitionName,
        ["eval_suites.name"] = DefinitionName,
        ["experiments.agent_name"] = DefinitionName,
        ["experiments.name"] = DefinitionName,
        ["inbound_triggers.name"] = DefinitionName,
        ["job_schedules.name"] = DefinitionName,
        ["mcp_servers.name"] = DefinitionName,
        ["quota_usage.agent_name"] = DefinitionName,
        ["quotas.agent_name"] = DefinitionName,
        ["skill_script_grants.script_name"] = DefinitionName,
        ["skill_script_grants.skill_name"] = DefinitionName,
        ["tool_approval_rules.agent_name"] = DefinitionName,
        ["tool_approval_rules.tool_name"] = DefinitionName,
        ["webhook_subscriptions.name"] = DefinitionName,
        ["workflow_definition_versions.name"] = DefinitionName,
        ["workflows.name"] = DefinitionName,
    };

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
        var rows = await context.ScalarAsync<string>(
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
