using AgentPrism.Sqlite.IntegrationTests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Sqlite.IntegrationTests;

/// <summary>Behavior of the migration runner on SQLite.</summary>
public sealed class MigrationRunnerTests(SqliteFixture fixture)
{
    /// <summary>Number of embedded migrations.</summary>
    private static int EmbeddedMigrationCount { get; } = typeof(MigrationRunner).Assembly
        .GetManifestResourceNames()
        .Count(static name =>
            name.StartsWith("AgentPrism.Sqlite.Migrations.", StringComparison.Ordinal)
            && name.EndsWith(".sql", StringComparison.Ordinal));

    [Fact]
    public async Task Tables_are_created_on_the_first_run()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture, applyMigrations: false);

        var applied = await context.Migrations.ApplyAsync();

        applied.ShouldBe(EmbeddedMigrationCount);

        var tableCount = await context.ScalarAsync<long>($"""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table' AND name LIKE '{context.TablePrefix}%' ESCAPE '\';
            """);

        // 0001_initial establishes the PostgreSQL 0001-0013 accumulation: 35
        // tables + migration ledger = 36. 0002_retention adds 2 more tables
        // (Phase 25): retention_policies, retention_runs. The count must be
        // the SAME across the other providers; all three carry the same data
        // model. The count is DELIBERATELY hard-coded: this test breaks when
        // a new table is added, and whoever added it notices the table.
        // Phase 29 added the `voice_sessions` table: 38 -> 39.
        // Phase 31 added the `run_scores` table: 39 -> 40.
        // Phase 42 added the `singleton_leases` table: 40 -> 41.
        // Phase 43 added the `idempotency_keys` table: 41 -> 42.
        // Phase 47 added the `run_inputs` table: 42 -> 43.
        // Phase 53 added the `api_keys` table: 43 -> 44.
        // Phase 55 added the `pending_approvals` table: 44 -> 45.
        // Phase 65 added `tenant_provider_bindings` and `tenant_egress_policies`: 45 -> 47.
        // Phase 66 added the `inbound_triggers` table: 47 -> 48.
        tableCount.ShouldBe(48);
    }

    [Fact]
    public async Task Second_run_applies_nothing()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture, applyMigrations: false);

        (await context.Migrations.ApplyAsync()).ShouldBe(EmbeddedMigrationCount);
        (await context.Migrations.ApplyAsync()).ShouldBe(0);
        (await context.Migrations.ApplyAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Applied_migrations_are_recorded_in_the_ledger()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

        var recorded = await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {context.TablePrefix}__migrations;");

        recorded.ShouldBe(EmbeddedMigrationCount);

        var checksum = await context.ScalarAsync<string>(
            $"SELECT checksum FROM {context.TablePrefix}__migrations WHERE id = 1;");

        checksum.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_modified_migration_throws()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

        // Corrupting the checksum in the ledger produces the same result as
        // modifying the file.
        await context.ExecuteAsync(
            $"UPDATE {context.TablePrefix}__migrations SET checksum = 'CORRUPTED' WHERE id = 1;");

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await context.Migrations.ApplyAsync());

        exception.Message.ShouldContain("has changed");
    }

    /// <summary>
    /// Five concurrent runners pass through the sidecar file lock one at a
    /// time, and the migrations are applied exactly once in total. Five
    /// contexts that share the same database file (fixture) but use separate
    /// table prefixes race for the same lock file.
    /// </summary>
    [Fact]
    public async Task Five_concurrent_runs_apply_migrations_exactly_once()
    {
        var tablePrefix = SqliteTestContext.NewTablePrefix();
        var contexts = new List<SqliteTestContext>();

        try
        {
            for (var index = 0; index < 5; index++)
            {
                contexts.Add(SqliteTestContext.Create(fixture, tablePrefix));
            }

            var results = await Task.WhenAll(contexts.Select(static async context =>
                await context.Migrations.ApplyAsync().AsTask()));

            // Exactly one runner applies them; the others wait for the lock
            // and find the ledger already full.
            results.Count(count => count == EmbeddedMigrationCount).ShouldBe(1);
            results.Sum().ShouldBe(EmbeddedMigrationCount);
        }
        finally
        {
            foreach (var context in contexts)
            {
                await context.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// K-389: the migration lock file is scoped to the table prefix. While
    /// the lock for prefix A is held, the lock for prefix B must be
    /// acquirable IMMEDIATELY; with a single shared lock file, B would wait
    /// for A to release.
    /// </summary>
    [Fact]
    public async Task Migration_locks_for_different_prefixes_do_not_block_each_other()
    {
        var dialectA = new SqliteDialect(SqliteTestContext.NewTablePrefix());
        var dialectB = new SqliteDialect(SqliteTestContext.NewTablePrefix());

        await using var connectionA = new SqliteConnection(fixture.ConnectionString);
        await connectionA.OpenAsync();
        await dialectA.AcquireMigrationLockAsync(connectionA, 30, CancellationToken.None);

        try
        {
            await using var connectionB = new SqliteConnection(fixture.ConnectionString);
            await connectionB.OpenAsync();

            var acquireB = dialectB.AcquireMigrationLockAsync(connectionB, 30, CancellationToken.None).AsTask();
            var winner = await Task.WhenAny(acquireB, Task.Delay(TimeSpan.FromSeconds(5)));

            winner.ShouldBe(acquireB);

            await dialectB.ReleaseMigrationLockAsync(connectionB, 30, CancellationToken.None);
        }
        finally
        {
            await dialectA.ReleaseMigrationLockAsync(connectionA, 30, CancellationToken.None);
        }
    }

    [Fact]
    public void An_invalid_table_prefix_is_rejected()
    {
        // Uppercase letters, quotes, and dots are rejected: the prefix is
        // embedded directly into SQL text, and the injection surface is
        // closed here (the SQLite counterpart of K-029).
        foreach (var invalid in new[] { "Agent", "agent-prism", "agent.prism", "agent prism", "dbo';--" })
        {
            Should.Throw<AgentPrismException>(() => new SqliteDialect(invalid));
        }
    }

    [Fact]
    public async Task A_custom_table_prefix_is_used()
    {
        var tablePrefix = SqliteTestContext.NewTablePrefix();

        await using var context = SqliteTestContext.Create(fixture, tablePrefix);
        await context.Migrations.ApplyAsync();

        var exists = await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{tablePrefix}tenants';");

        exists.ShouldBe(1);
    }

    [Fact]
    public void Migration_runner_reads_from_the_correct_assembly()
    {
        // The migration resource prefix is provider-specific; applying
        // PostgreSQL/SQL Server files to SQLite would silently build the
        // wrong schema.
        var dialect = new SqliteDialect("agentprism_");

        dialect.MigrationResourcePrefix.ShouldBe("AgentPrism.Sqlite.Migrations.");
        MigrationDescriptor
            .Discover(dialect.GetType().Assembly, dialect.MigrationResourcePrefix)
            .Count.ShouldBe(EmbeddedMigrationCount);
    }

    [Fact]
    public void Migration_runner_cannot_be_constructed_with_an_invalid_prefix()
    {
        var exception = Should.Throw<AgentPrismException>(() =>
            new MigrationRunner(
                new SqlStoreContext
                {
                    DataSource = new SqliteDataSource(fixture.ConnectionString),
                    Dialect = new SqliteDialect("PUBLIC"),
                    CommandTimeoutSeconds = 30,
                    ProviderName = "SQLite",
                },
                NullLogger<MigrationRunner>.Instance));

        exception.Message.ShouldContain("schema name");
    }
}
