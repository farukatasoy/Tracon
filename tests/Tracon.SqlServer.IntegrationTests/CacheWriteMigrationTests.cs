using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// The five cache-write columns of <c>runs</c> arrive with migration 0054. A row written
/// before them has NULL in all five, and every read that carries a run (get, list, statistics, tree)
/// must still answer for it.
/// </summary>
/// <remarks>
/// The row is written under the PRE-upgrade schema (every migration before this one) and read back
/// through the real store after the upgrade: the reader names the new columns by ordinal, so a run
/// with NULL there is the case a struct field left at <c>default</c> once took a whole list endpoint down.
/// </remarks>
public sealed class CacheWriteMigrationTests(SqlServerFixture fixture)
{
    private const int MigrationId = 54;

    private const string Tenant = "default";

    [Fact]
    public async Task A_run_written_before_the_columns_existed_reads_back_with_null_write_fields_through_every_read()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();
        await using var context = SqlServerTestContext.Create(fixture, schemaName);
        var prefix = $"{schemaName}.";

        await context.ExecuteAsync($"IF SCHEMA_ID(N'{schemaName}') IS NULL EXEC(N'CREATE SCHEMA {schemaName}');");

        await ApplyThroughAsync(context, prefix, MigrationId - 1);

        var legacy = Guid.NewGuid();
        var child = Guid.NewGuid();

        await InsertAsync(context, prefix, legacy, parent: null);
        await InsertAsync(context, prefix, child, parent: legacy);

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'{schemaName}.runs') AND name LIKE N'cache[_]write%';"))
            .ShouldBe(5);

        var run = (await context.Runs.GetRunAsync(legacy)).ShouldNotBeNull();

        run.Usage.ShouldNotBeNull().InputTokens.ShouldBe(1_000);
        run.Usage.CachedInputTokens.ShouldBe(200);
        run.Usage.CacheWriteInputTokens.ShouldBeNull();
        run.Usage.CacheWriteLongInputTokens.ShouldBeNull();
        run.Cost.ShouldNotBeNull().Total().ShouldBe(3.5m);
        run.Cost.CacheWriteCost.ShouldBeNull();
        run.Cost.CacheWritePricePerMillionTokens.ShouldBeNull();
        run.Cost.CacheWriteLongPricePerMillionTokens.ShouldBeNull();

        // The tree adds the child's (old) cost and tokens, and its write fields are null all the same.
        run.TreeUsage.ShouldNotBeNull().InputTokens.ShouldBe(2_000);
        run.TreeUsage.CacheWriteInputTokens.ShouldBeNull();
        run.TreeCost.ShouldNotBeNull().Total().ShouldBe(7m);
        run.TreeCost.CacheWriteCost.ShouldBeNull();

        var listed = await context.Runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false });
        listed.Count.ShouldBe(2);
        listed.ShouldAllBe(static row => row.Usage!.CacheWriteInputTokens == null && row.Cost!.CacheWriteCost == null);

        var statistics = await context.Runs.GetStatisticsAsync(new RunStatisticsQuery { TenantId = Tenant });
        statistics.TotalCost.ShouldBe(7m);
        statistics.CacheWriteInputTokens.ShouldBe(0);
        statistics.CacheWriteLongInputTokens.ShouldBe(0);
    }

    [Fact]
    public async Task A_run_written_after_the_upgrade_adds_to_the_totals_beside_the_old_one()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();
        await using var context = SqlServerTestContext.Create(fixture, schemaName);
        var prefix = $"{schemaName}.";

        await context.ExecuteAsync($"IF SCHEMA_ID(N'{schemaName}') IS NULL EXEC(N'CREATE SCHEMA {schemaName}');");

        await ApplyThroughAsync(context, prefix, MigrationId - 1);
        await InsertAsync(context, prefix, Guid.NewGuid(), parent: null);
        await context.Migrations.ApplyAsync();

        var fresh = Guid.NewGuid();

        await context.Runs.StartRunAsync(new RunStartInfo
        {
            RunId = fresh,
            AgentName = "after-upgrade",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = Tenant,
            ModelId = "claude-x",
        });

        await context.Runs.CompleteRunAsync(new RunCompletion
        {
            RunId = fresh,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            TenantId = Tenant,
            Usage = new RunUsage { InputTokens = 1_000, OutputTokens = 100, TotalTokens = 1_100, CacheWriteInputTokens = 600, CacheWriteLongInputTokens = 300 },
            Cost = new RunCost
            {
                InputCost = 0.1m,
                OutputCost = 0.2m,
                CacheWriteCost = 1.35m,
                CacheWritePricePerMillionTokens = 1_250m,
                CacheWriteLongPricePerMillionTokens = 2_000m,
                Currency = "USD",
                Source = PricingSource.Catalog,
            },
        });

        var run = (await context.Runs.GetRunAsync(fresh)).ShouldNotBeNull();

        run.Usage.ShouldNotBeNull().CacheWriteInputTokens.ShouldBe(600);
        run.Usage.CacheWriteLongInputTokens.ShouldBe(300);
        run.Cost.ShouldNotBeNull().CacheWriteCost.ShouldBe(1.35m);
        run.Cost.Total().ShouldBe(1.65m);

        var statistics = await context.Runs.GetStatisticsAsync(new RunStatisticsQuery { TenantId = Tenant });
        statistics.TotalCost.ShouldBe(3.5m + 1.65m);
        statistics.CacheWriteInputTokens.ShouldBe(600);
    }

    /// <summary>
    /// A completed run in the shape every row had before migration 0054: tokens, a three-term cost and
    /// a cache read, no cache-write column.
    /// </summary>
    private static async Task InsertAsync(SqlServerTestContext context, string prefix, Guid runId, Guid? parent)
        => await context.ExecuteAsync(
            $"INSERT INTO {prefix}runs (id, tenant_id, agent_name, status, started_at, completed_at, is_streaming, " +
            "input_tokens, output_tokens, total_tokens, cached_input_tokens, input_cost, output_cost, cached_input_cost, " +
            "cost_currency, pricing_source, model_id, parent_run_id, root_run_id, depth) " +
            $"VALUES ('{runId}', N'{Tenant}', N'legacy-agent', 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 0, " +
            "1000, 100, 1100, 200, 1.0, 2.0, 0.5, N'USD', 1, N'claude-x', " +
            (parent is { } root ? $"'{root}', '{root}', 1" : "NULL, NULL, 0") +
            ");");

    private static async Task ApplyThroughAsync(SqlServerTestContext context, string prefix, int lastId)
    {
        var migrations = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.SqlServer.Migrations.")
            .Where(migration => migration.Id <= lastId)
            .OrderBy(migration => migration.Id)
            .ToArray();

        migrations.ShouldNotBeEmpty();

        await context.ExecuteAsync(context.StoreContext.Sql.CreateMigrationsTable);

        foreach (var migration in migrations)
        {
            await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteAsync(
                $"INSERT INTO {prefix}__migrations (set_name, id, name, checksum, applied_at) " +
                $"VALUES ('core', {migration.Id}, '{migration.Name}', '{migration.Checksum}', SYSDATETIMEOFFSET());");
        }
    }
}
