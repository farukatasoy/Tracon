using Tracon.SqlServer.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// The owner of a job lease and the owner of a singleton lease compare ordinally: applied over leases the older
/// release wrote, with both spellings of one name held at once.
/// </summary>
/// <remarks>
/// <para>
/// Neither column is part of a key, so the proof that nothing merges is the row count and the values, read before and
/// after, and the proof that nothing is lost is a lease that goes on working: the worker that holds it renews it and
/// the worker with the look-alike name does not.
/// </para>
/// </remarks>
public sealed class LeaseOwnerColumnUpgradeTests(SqlServerFixture fixture)
{
    private const string Ordinal = "Latin1_General_BIN2";

    private const string Migration = "ordinal_lease_owner";

    private static readonly (string Table, string Column)[] OwnerColumns = [("jobs", "lease_owner"), ("singleton_leases", "owner_id")];

    [Fact]
    public async Task Leases_written_before_the_upgrade_keep_their_count_their_values_and_their_holder()
    {
        await using var context = await OpenOldSchemaAsync();

        var before = await RowCountsAsync(context);

        before.Where(static pair => pair.Value == 0).Select(static pair => pair.Key)
            .ShouldBeEmpty("the seed has to put a row in every table the migration changes, or its count proves nothing");

        // The old comparison folds the case: the second spelling is the first one, which is the defect.
        (await CountWhereAsync(context, "jobs", "lease_owner", "worker-a")).ShouldBe(2, "the old comparison folds the two workers");
        (await CountWhereAsync(context, "singleton_leases", "owner_id", "node-a")).ShouldBe(2, "the old comparison folds the two nodes");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await RowCountsAsync(context)).ShouldBe(before, "the migration neither adds, removes nor merges a row");

        // Each spelling finds its own rows and only those.
        (await CountWhereAsync(context, "jobs", "lease_owner", "Worker-A")).ShouldBe(1);
        (await CountWhereAsync(context, "jobs", "lease_owner", "worker-a")).ShouldBe(1);
        (await CountWhereAsync(context, "singleton_leases", "owner_id", "Node-A")).ShouldBe(1);
        (await CountWhereAsync(context, "singleton_leases", "owner_id", "node-a")).ShouldBe(1);

        // A lease that was running keeps working for the worker that holds it.
        var upper = (await context.Jobs.QueryAsync(new JobQuery { TenantId = "upper" })).ShouldHaveSingleItem();
        var lower = (await context.Jobs.QueryAsync(new JobQuery { TenantId = "lower" })).ShouldHaveSingleItem();

        upper.LeaseOwner.ShouldBe("Worker-A");
        lower.LeaseOwner.ShouldBe("worker-a");
        (await context.Jobs.RenewLeaseAsync(JobLease.Of(upper), TimeSpan.FromMinutes(5))).ShouldBeTrue();
        (await context.Jobs.RenewLeaseAsync(JobLease.Of(upper) with { Owner = "worker-a" }, TimeSpan.FromMinutes(5))).ShouldBeFalse();
        (await context.SingletonLeases.RenewAsync("lease-upper", "Node-A", TimeSpan.FromMinutes(5))).ShouldBeTrue();
        (await context.SingletonLeases.RenewAsync("lease-upper", "node-a", TimeSpan.FromMinutes(5))).ShouldBeFalse();
    }

    [Fact]
    public async Task Every_owner_column_compares_ordinally_once_the_migration_ran()
    {
        await using var context = await OpenOldSchemaAsync();

        foreach (var (table, column) in OwnerColumns)
        {
            AssertCollation(await CollationOfAsync(context, table, column), ordinal: false, $"{table}.{column} folds the case before the migration");
        }

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        foreach (var (table, column) in OwnerColumns)
        {
            AssertCollation(await CollationOfAsync(context, table, column), ordinal: true, $"{table}.{column} holds the name of a lease owner");
        }
    }

    [Fact]
    public async Task The_migration_keeps_the_nullability_of_both_columns()
    {
        await using var context = await OpenOldSchemaAsync();

        var before = await NullabilityAsync(context);

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await NullabilityAsync(context)).ShouldBe(before);
        before.ShouldBe("jobs.lease_owner:1;singleton_leases.owner_id:0");
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

        foreach (var (table, column) in OwnerColumns)
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
            $"WHERE SCHEMA_NAME(t.schema_id) = N'{context.SchemaName}' AND ((t.name = N'jobs' AND c.name = N'lease_owner') OR (t.name = N'singleton_leases' AND c.name = N'owner_id'));");

    private static async ValueTask<long> CountWhereAsync(SqlServerTestContext context, string table, string column, string value)
        => await context.ScalarAsync<long>($"SELECT CAST(COUNT(*) AS bigint) FROM {context.SchemaName}.{table} WHERE {column} = N'{value}';");

    /// <summary>The row count of every table the migration changes.</summary>
    private static async ValueTask<SortedDictionary<string, long>> RowCountsAsync(SqlServerTestContext context)
    {
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);

        foreach (var table in OwnerColumns.Select(static pair => pair.Table))
        {
            counts[table] = await context.ScalarAsync<long>($"SELECT CAST(COUNT(*) AS bigint) FROM {context.SchemaName}.{table};");
        }

        return counts;
    }

    /// <summary>
    /// Builds the schema as it was before the migration and writes into it the way the older release did: two jobs
    /// leased by workers whose names differ in letter case, and two singleton leases held the same way.
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
        foreach (var (tenant, owner) in new[] { ("upper", "Worker-A"), ("lower", "worker-a") })
        {
            await context.Jobs.EnqueueAsync(TestData.Job(tenant), ["a"]);
            (await context.Jobs.LeaseAsync(new JobLeaseRequest { Owner = owner, LeaseDuration = TimeSpan.FromMinutes(5) }))
                .ShouldNotBeNull();
        }

        (await context.SingletonLeases.TryAcquireAsync("lease-upper", "Node-A", TimeSpan.FromMinutes(5))).ShouldBeTrue();
        (await context.SingletonLeases.TryAcquireAsync("lease-lower", "node-a", TimeSpan.FromMinutes(5))).ShouldBeTrue();
    }
}
