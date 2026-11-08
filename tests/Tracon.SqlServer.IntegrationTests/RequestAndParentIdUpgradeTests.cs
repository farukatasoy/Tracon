using System.Text.Json;
using Microsoft.Data.SqlClient;
using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// The id of an approval request and the parent of a workflow checkpoint compare ordinally: applied over rows
/// written with both spellings of one id.
/// </summary>
/// <remarks>
/// <para>
/// Neither column is part of a key, so the proof that nothing merges is the row count and the values, read before and
/// after. The proof that the change is worth making is the comparison the two columns exist for: the parent of a
/// checkpoint is the id of an earlier checkpoint, and that comparison stops with a collation conflict while the two
/// columns carry different collations.
/// </para>
/// </remarks>
public sealed class RequestAndParentIdUpgradeTests(SqlServerFixture fixture)
{
    private const string Ordinal = "Latin1_General_BIN2";

    private const string Migration = "ordinal_request_and_parent_ids";

    private const string Tenant = "default";

    private const string Session = "Flow-Session";

    private const int CollationConflict = 468;

    private static readonly Guid RunId = new("aaaaaaaa-2222-3333-4444-555555555555");

    private static readonly (string Table, string Column)[] ChangedColumns =
        [("pending_approvals", "request_id"), ("workflow_checkpoints", "parent_id")];

    [Fact]
    public async Task Rows_written_before_the_upgrade_keep_their_count_and_their_values()
    {
        await using var context = await OpenOldSchemaAsync();

        var before = await RowCountsAsync(context);

        before.Where(static pair => pair.Value == 0).Select(static pair => pair.Key)
            .ShouldBeEmpty("the seed has to put a row in every table the migration changes, or its count proves nothing");

        // The old comparison folds the case: the second spelling finds the first one's rows, which is the defect.
        (await CountWhereAsync(context, "pending_approvals", "request_id", "req-a")).ShouldBe(2, "the old comparison folds the two requests");
        (await CountWhereAsync(context, "workflow_checkpoints", "parent_id", "checkpoint-a")).ShouldBe(2, "the old comparison folds the two parents");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await RowCountsAsync(context)).ShouldBe(before, "the migration neither adds, removes nor merges a row");

        // Each spelling finds its own rows and only those.
        (await CountWhereAsync(context, "pending_approvals", "request_id", "Req-A")).ShouldBe(1);
        (await CountWhereAsync(context, "pending_approvals", "request_id", "req-a")).ShouldBe(1);
        (await CountWhereAsync(context, "workflow_checkpoints", "parent_id", "Checkpoint-A")).ShouldBe(1);
        (await CountWhereAsync(context, "workflow_checkpoints", "parent_id", "checkpoint-a")).ShouldBe(1);

        // The values come back as they were written, in their own letter case.
        var parents = (await context.WorkflowCheckpoints.ListAsync(Tenant, Session)).ToDictionary(static record => record.CheckpointId, static record => record.ParentCheckpointId, StringComparer.Ordinal);

        parents["Child-Upper"].ShouldBe("Checkpoint-A");
        parents["Child-Lower"].ShouldBe("checkpoint-a");
        parents["Checkpoint-A"].ShouldBeNull();
        (await context.ScalarAsync<string>(
            $"SELECT MIN(request_id) FROM {context.SchemaName}.pending_approvals WHERE request_id LIKE N'Req%';")).ShouldBe("Req-A");
    }

    [Fact]
    public async Task Both_columns_compare_ordinally_once_the_migration_ran()
    {
        await using var context = await OpenOldSchemaAsync();

        foreach (var (table, column) in ChangedColumns)
        {
            AssertCollation(await CollationOfAsync(context, table, column), ordinal: false, $"{table}.{column} folds the case before the migration");
        }

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        foreach (var (table, column) in ChangedColumns)
        {
            AssertCollation(await CollationOfAsync(context, table, column), ordinal: true, $"{table}.{column} carries an id of its own");
        }
    }

    [Fact]
    public async Task The_parent_of_a_checkpoint_meets_the_checkpoint_it_names_only_after_the_migration_ran()
    {
        await using var context = await OpenOldSchemaAsync();

        var join =
            $"SELECT CAST(COUNT(*) AS bigint) FROM {context.SchemaName}.workflow_checkpoints child " +
            $"JOIN {context.SchemaName}.workflow_checkpoints parent ON child.parent_id = parent.checkpoint_id;";

        // Before: the parent is compared by the collation of the database and the checkpoint by its characters.
        var conflict = await Should.ThrowAsync<SqlException>(async () => await context.ScalarAsync<long>(join));

        conflict.Number.ShouldBe(CollationConflict);

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        // After: each child finds the one parent it names, not the look-alike.
        (await context.ScalarAsync<long>(join)).ShouldBe(2);
    }

    [Fact]
    public async Task The_migration_keeps_the_nullability_of_both_columns()
    {
        await using var context = await OpenOldSchemaAsync();

        var before = await NullabilityAsync(context);

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await NullabilityAsync(context)).ShouldBe(before);
        before.ShouldBe("pending_approvals.request_id:0;workflow_checkpoints.parent_id:1");
    }

    [Fact]
    public async Task The_migration_can_run_again_over_its_own_result()
    {
        await using var context = await OpenOldSchemaAsync();

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        var migration = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.SqlServer.Migrations.")
            .Single(static migration => migration.Name.Contains(Migration, StringComparison.Ordinal));

        var before = await RowCountsAsync(context);

        // A run that wrote its work but not its ledger line starts over from the top.
        await context.ExecuteRetryingDeadlockAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));

        (await RowCountsAsync(context)).ShouldBe(before);

        foreach (var (table, column) in ChangedColumns)
        {
            AssertCollation(await CollationOfAsync(context, table, column), ordinal: true, $"{table}.{column} stays ordinal");
        }
    }

    private static void AssertCollation(string? actual, bool ordinal, string because)
        => string.Equals(actual, Ordinal, StringComparison.Ordinal).ShouldBe(ordinal, $"{because} (collation: {actual})");

    private static ValueTask<string?> CollationOfAsync(SqlServerTestContext context, string table, string column)
        => context.CatalogScalarAsync<string>(
            $"SELECT collation_name FROM sys.columns WHERE object_id = OBJECT_ID(N'{context.SchemaName}.{table}') AND name = N'{column}';");

    private static async ValueTask<string?> NullabilityAsync(SqlServerTestContext context)
        => await context.CatalogScalarAsync<string>(
            "SELECT STRING_AGG(CAST(t.name AS nvarchar(100)) + N'.' + CAST(c.name AS nvarchar(100)) + N':' + CAST(c.is_nullable AS nvarchar(1)), N';') " +
            "WITHIN GROUP (ORDER BY t.name) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id " +
            $"WHERE SCHEMA_NAME(t.schema_id) = N'{context.SchemaName}' AND ((t.name = N'pending_approvals' AND c.name = N'request_id') OR (t.name = N'workflow_checkpoints' AND c.name = N'parent_id'));");

    private static async ValueTask<long> CountWhereAsync(SqlServerTestContext context, string table, string column, string value)
        => await context.ScalarAsync<long>($"SELECT CAST(COUNT(*) AS bigint) FROM {context.SchemaName}.{table} WHERE {column} = N'{value}';");

    /// <summary>The row count of every table the migration changes.</summary>
    private static async ValueTask<SortedDictionary<string, long>> RowCountsAsync(SqlServerTestContext context)
    {
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);

        foreach (var table in ChangedColumns.Select(static pair => pair.Table))
        {
            counts[table] = await context.ScalarAsync<long>($"SELECT CAST(COUNT(*) AS bigint) FROM {context.SchemaName}.{table};");
        }

        return counts;
    }

    /// <summary>
    /// Builds the schema as it was before the migration and writes into it the way the older release did: two approvals whose
    /// request ids differ in letter case, and two checkpoints that name two look-alike parents.
    /// </summary>
    private async Task<SqlServerTestContext> OpenOldSchemaAsync()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();
        var context = SqlServerTestContext.Create(fixture, schemaName);
        var all = MigrationDescriptor.Discover(typeof(MigrationRunner).Assembly, "Tracon.SqlServer.Migrations.");
        var target = all.Single(static migration => migration.Name.Contains(Migration, StringComparison.Ordinal)).Id;
        var before = all.Where(migration => migration.Id < target).OrderBy(migration => migration.Id).ToArray();

        before.ShouldNotBeEmpty("the pre-upgrade migration set has to exist");

        await context.ExecuteAsync($"IF SCHEMA_ID(N'{schemaName}') IS NULL EXEC(N'CREATE SCHEMA {schemaName}');");
        await context.ExecuteAsync(context.StoreContext.Sql.CreateMigrationsTable);

        foreach (var migration in before)
        {
            await context.ExecuteRetryingDeadlockAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteRetryingDeadlockAsync(
                $"INSERT INTO {schemaName}.__migrations (set_name, id, name, checksum, applied_at) " +
                $"VALUES ('core', {migration.Id}, '{migration.Name}', '{migration.Checksum}', SYSDATETIMEOFFSET());");
        }

        await SeedAsync(context);

        return context;
    }

    private static async ValueTask SeedAsync(SqlServerTestContext context)
    {
        var schema = context.SchemaName;

        await context.ExecuteAsync(
            $"INSERT INTO {schema}.runs (id, tenant_id, agent_name, session_id, status, started_at, is_streaming) " +
            $"VALUES ('{RunId}', N'{Tenant}', N'writer', N'{Session}', 2, SYSDATETIMEOFFSET(), 0);");

        foreach (var requestId in new[] { "Req-A", "req-a" })
        {
            await context.ExecuteAsync(
                $"INSERT INTO {schema}.pending_approvals (id, tenant_id, run_id, session_id, request_id, tool_name, status, expires_at, created_at) " +
                $"VALUES (NEWID(), N'{Tenant}', '{RunId}', N'{Session}', N'{requestId}', N'tool', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());");
        }

        // The checkpoint id compares ordinally already (the parent of a checkpoint is one of these ids), so both spellings of
        // the parent are real checkpoints.
        foreach (var (checkpointId, parent) in new (string, string?)[]
        {
            ("Checkpoint-A", null), ("checkpoint-a", null), ("Child-Upper", "Checkpoint-A"), ("Child-Lower", "checkpoint-a"),
        })
        {
            await context.WorkflowCheckpoints.CreateAsync(new WorkflowCheckpointRecord
            {
                Id = TraconId.NewId(),
                TenantId = Tenant,
                SessionId = Session,
                CheckpointId = checkpointId,
                ParentCheckpointId = parent,
                CreatedAt = DateTimeOffset.UtcNow,
                State = JsonDocument.Parse("""{"stepNumber":1}""").RootElement.Clone(),
            });
        }
    }
}
