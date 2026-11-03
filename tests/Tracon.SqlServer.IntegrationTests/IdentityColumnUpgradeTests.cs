using Tracon.SqlServer.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// The identity of a user, the name of an experiment variant and the actor of an audit entry compare ordinally:
/// applied over rows written under the case-insensitive comparison, and stopped halfway.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 A migration that drops an index, changes the column under it and builds the index again can lose a row
/// or a value only on rows that already exist. No compiler and no SQL text snapshot sees migration SQL, so
/// the upgrade runs against rows the older release wrote, and the rows hold the case that matters: both
/// spellings of one identity, side by side.
/// </para>
/// <para>
/// None of the columns is part of a key, so the proof that nothing merges is the row count and the values,
/// read before and after.
/// </para>
/// </remarks>
public sealed class IdentityColumnUpgradeTests(SqlServerFixture fixture)
{
    private const string Ordinal = "Latin1_General_BIN2";

    private const string Migration = "ordinal_identity_columns";

    private static readonly Guid ExperimentId = new("aaaaaaaa-1111-2222-3333-444444444444");

    private static readonly Guid RunUpper = new("bbbbbbbb-1111-2222-3333-444444444444");

    private static readonly Guid RunLower = new("bbbbbbbb-2222-2222-3333-444444444444");

    /// <summary>The columns the migration changes, as table and column.</summary>
    private static readonly (string Table, string Column)[] IdentityColumns =
    [
        ("sessions", "owner_id"), ("runs", "user_id"), ("runs", "variant"), ("audit_log", "actor"),
    ];

    /// <summary>Every index the migration drops and builds again.</summary>
    private static readonly string[] RebuiltObjects =
    [
        "sessions_tenant_owner_updated_idx", "runs_tenant_user_idx", "runs_experiment_idx",
    ];

    [Fact]
    public async Task Rows_written_before_the_upgrade_keep_their_count_and_their_values()
    {
        await using var context = await OpenOldSchemaAsync();

        var before = await RowCountsAsync(context);

        before.Where(static pair => pair.Value == 0).Select(static pair => pair.Key)
            .ShouldBeEmpty("the seed has to put a row in every table the migration changes, or its count proves nothing");

        // The old comparison folds the case: the second spelling is the first one, which is the defect.
        (await CountWhereAsync(context, "sessions", "owner_id", "alice")).ShouldBe(2, "the old comparison folds the two owners");
        (await CountWhereAsync(context, "runs", "user_id", "alice")).ShouldBe(2, "the old comparison folds the two users");
        (await CountWhereAsync(context, "runs", "variant", "a")).ShouldBe(2, "the old comparison folds the two variants");
        (await CountWhereAsync(context, "audit_log", "actor", "admin")).ShouldBe(2, "the old comparison folds the two actors");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await RowCountsAsync(context)).ShouldBe(before, "the migration neither adds, removes nor merges a row");

        // Each spelling finds its own rows and only those; the values are the ones that were written.
        (await CountWhereAsync(context, "sessions", "owner_id", "alice")).ShouldBe(1);
        (await CountWhereAsync(context, "sessions", "owner_id", "Alice")).ShouldBe(1);
        (await CountWhereAsync(context, "runs", "user_id", "alice")).ShouldBe(1);
        (await CountWhereAsync(context, "runs", "user_id", "Alice")).ShouldBe(1);
        (await CountWhereAsync(context, "runs", "variant", "a")).ShouldBe(1);
        (await CountWhereAsync(context, "runs", "variant", "A")).ShouldBe(1);
        (await CountWhereAsync(context, "audit_log", "actor", "admin")).ShouldBe(1);
        (await CountWhereAsync(context, "audit_log", "actor", "Admin")).ShouldBe(1);

        (await context.Sessions.GetAsync("Session-Alice")).ShouldNotBeNull().OwnerId.ShouldBe("Alice");
        (await context.Sessions.GetAsync("Session-alice")).ShouldNotBeNull().OwnerId.ShouldBe("alice");
        (await context.Runs.GetRunAsync(RunUpper)).ShouldNotBeNull().UserId.ShouldBe("Alice");
        (await context.Runs.GetRunAsync(RunLower)).ShouldNotBeNull().UserId.ShouldBe("alice");

        // The stores now answer the way the other engines do.
        (await context.Sessions.QueryAsync(new SessionQuery { OwnerId = "alice" })).ShouldHaveSingleItem().Id.ShouldBe("Session-alice");
        (await context.Runs.QueryRunsAsync(new RunQuery { UserId = "alice" })).ShouldHaveSingleItem().Id.ShouldBe(RunLower);
        (await context.Runs.GetExperimentResultsAsync(new ExperimentResultsQuery { ExperimentId = ExperimentId })).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Every_column_that_carries_an_identity_compares_ordinally_once_the_migration_ran()
    {
        await using var context = await OpenOldSchemaAsync();

        foreach (var (table, column) in IdentityColumns)
        {
            AssertCollation(await CollationOfAsync(context, table, column), ordinal: false, $"{table}.{column} folds the case before the migration");
        }

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        foreach (var (table, column) in IdentityColumns)
        {
            AssertCollation(await CollationOfAsync(context, table, column), ordinal: true, $"{table}.{column} carries an identity");
        }
    }

    [Fact]
    public async Task A_migration_that_fails_on_its_last_column_leaves_the_old_shape_everywhere()
    {
        await using var context = await OpenOldSchemaAsync();

        // A schema-bound view makes the change of the last column impossible, after every earlier column was
        // already rebuilt inside the same transaction.
        await context.ExecuteAsync(
            $"CREATE VIEW {context.SchemaName}.audit_actors WITH SCHEMABINDING AS SELECT id, actor FROM {context.SchemaName}.audit_log;");

        await Should.ThrowAsync<TraconException>(async () => await context.Migrations.ApplyAsync());

        foreach (var (table, column) in IdentityColumns)
        {
            AssertCollation(await CollationOfAsync(context, table, column), ordinal: false, $"{table}.{column} rolled back with the last column");
        }

        (await ShapesAsync(context)).Count.ShouldBe(RebuiltObjects.Length, "the indexes the migration dropped are back");
        (await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM {context.SchemaName}.__migrations WHERE name LIKE N'%{Migration}%';"))
            .ShouldBe(0, "a failed migration is not in the ledger");

        // Nothing is half done: the next run, without the view, goes through.
        await context.ExecuteAsync($"DROP VIEW {context.SchemaName}.audit_actors;");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        foreach (var (table, column) in IdentityColumns)
        {
            AssertCollation(await CollationOfAsync(context, table, column), ordinal: true, $"the retry changed {table}.{column}");
        }
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

        foreach (var (table, column) in IdentityColumns)
        {
            AssertCollation(await CollationOfAsync(context, table, column), ordinal: true, $"{table}.{column} stays ordinal");
        }

        (await ShapesAsync(context)).Count.ShouldBe(RebuiltObjects.Length);
    }

    [Fact]
    public async Task Every_index_comes_back_with_the_shape_it_had()
    {
        await using var context = await OpenOldSchemaAsync();

        var before = await ShapesAsync(context);

        before.Count.ShouldBe(RebuiltObjects.Length, "every index the migration rebuilds exists before it runs");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        // Name, kind, uniqueness, key columns in order (and their direction) and the filter: an index that came
        // back unique, a filtered index that came back unfiltered or a column that moved shows here.
        (await ShapesAsync(context)).ShouldBe(before);
    }

    private static void AssertCollation(string? actual, bool ordinal, string because)
        => string.Equals(actual, Ordinal, StringComparison.Ordinal).ShouldBe(ordinal, $"{because} (collation: {actual})");

    private static ValueTask<string?> CollationOfAsync(SqlServerTestContext context, string table, string column)
        => context.CatalogScalarAsync<string>(
            $"SELECT collation_name FROM sys.columns WHERE object_id = OBJECT_ID(N'{context.SchemaName}.{table}') AND name = N'{column}';");

    private static async ValueTask<IReadOnlyList<string>> ShapesAsync(SqlServerTestContext context)
    {
        var names = string.Join(", ", RebuiltObjects.Select(static name => $"N'{name}'"));

        var shapes = await context.CatalogScalarAsync<string>(
            "SELECT STRING_AGG(CAST(x.shape AS nvarchar(max)), N';') WITHIN GROUP (ORDER BY x.shape) FROM (" +
            "SELECT CAST(i.name AS nvarchar(200)) COLLATE DATABASE_DEFAULT + N'|' + CAST(i.type_desc AS nvarchar(60)) COLLATE DATABASE_DEFAULT + N'|' + " +
            "CAST(i.is_primary_key AS nvarchar(1)) + CAST(i.is_unique_constraint AS nvarchar(1)) + CAST(i.is_unique AS nvarchar(1)) + N'|' + " +
            "ISNULL((SELECT STRING_AGG(CAST(c.name AS nvarchar(200)) COLLATE DATABASE_DEFAULT + CASE WHEN ic.is_descending_key = 1 THEN N' DESC' ELSE N'' END, N',') WITHIN GROUP (ORDER BY ic.key_ordinal) " +
            "FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id " +
            "WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0), N'') + N'|' + " +
            "ISNULL(CAST(i.filter_definition AS nvarchar(400)) COLLATE DATABASE_DEFAULT, N'') AS shape " +
            "FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id " +
            $"WHERE SCHEMA_NAME(t.schema_id) = N'{context.SchemaName}' AND i.name IN ({names})) AS x;");

        return (shapes ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries);
    }

    private static async ValueTask<long> CountWhereAsync(SqlServerTestContext context, string table, string column, string value)
        => await context.ScalarAsync<long>($"SELECT CAST(COUNT(*) AS bigint) FROM {context.SchemaName}.{table} WHERE {column} = N'{value}';");

    /// <summary>The row count of every table the migration changes.</summary>
    private static async ValueTask<SortedDictionary<string, long>> RowCountsAsync(SqlServerTestContext context)
    {
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);

        foreach (var table in IdentityColumns.Select(static pair => pair.Table).Distinct(StringComparer.Ordinal))
        {
            counts[table] = await context.ScalarAsync<long>($"SELECT CAST(COUNT(*) AS bigint) FROM {context.SchemaName}.{table};");
        }

        return counts;
    }

    /// <summary>
    /// Builds the schema as it was before the migration and writes into it the way the older release did:
    /// two spellings of one owner, one user, one variant and one actor, side by side.
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
        var now = DateTimeOffset.UtcNow;

        await context.Sessions.SaveAsync(TestData.Session("Session-Alice") with { OwnerId = "Alice" });
        await context.Sessions.SaveAsync(TestData.Session("Session-alice") with { OwnerId = "alice" });
        await context.Sessions.SaveAsync(TestData.Session("Session-nobody"));

        foreach (var (runId, user, variant) in new[] { (RunUpper, "Alice", "A"), (RunLower, "alice", "a") })
        {
            await context.Runs.StartRunAsync(TestData.Run(runId, "Writer") with { UserId = user, ExperimentId = ExperimentId, Variant = variant });
            await context.Runs.CompleteRunAsync(new RunCompletion
            {
                RunId = runId,
                Status = RunStatus.Completed,
                CompletedAt = now,
                Usage = new RunUsage { TotalTokens = 10 },
            });
        }

        var schema = context.SchemaName;

        foreach (var actor in new[] { "Admin", "admin" })
        {
            await context.ExecuteAsync(
                $"INSERT INTO {schema}.audit_log (id, tenant_id, actor, action, entity, created_at) " +
                $"VALUES (NEWID(), N'default', N'{actor}', N'save', N'agent:Writer', SYSDATETIMEOFFSET());");
        }
    }
}
