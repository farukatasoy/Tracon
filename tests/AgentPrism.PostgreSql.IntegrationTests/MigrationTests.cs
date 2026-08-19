using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>The behavior of the migration runner.</summary>
public sealed class MigrationRunnerTests(PostgresFixture fixture)
{
    /// <summary>
    /// The count of embedded CORE migrations. Not written as a constant:
    /// every new migration file would break these tests, and the break would
    /// be unrelated to the behavior the test verifies.
    /// </summary>
    private static int EmbeddedCoreMigrationCount { get; } = typeof(MigrationRunner).Assembly
        .GetManifestResourceNames()
        .Count(static name =>
            name.StartsWith("AgentPrism.PostgreSql.Migrations.", StringComparison.Ordinal)
            && name.EndsWith(".sql", StringComparison.Ordinal));

    /// <summary>
    /// The count of embedded "knowledge" migrations (phase 67; needs
    /// <c>pgvector</c>). <see cref="PostgresTestContext"/> enables this set by
    /// default so the ~40 unrelated contract test classes stay unchanged.
    /// </summary>
    private static int EmbeddedKnowledgeMigrationCount { get; } = typeof(MigrationRunner).Assembly
        .GetManifestResourceNames()
        .Count(static name =>
            name.StartsWith("AgentPrism.PostgreSql.MigrationsKnowledge.", StringComparison.Ordinal)
            && name.EndsWith(".sql", StringComparison.Ordinal));

    /// <summary>The total embedded migration count when the knowledge set is enabled (the test default).</summary>
    private static int EmbeddedMigrationCount => EmbeddedCoreMigrationCount + EmbeddedKnowledgeMigrationCount;

    [Fact]
    public async Task First_run_creates_the_schema_and_tables()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture, applyMigrations: false);

        var applied = await context.Migrations.ApplyAsync();

        applied.ShouldBe(EmbeddedMigrationCount);

        var tableCount = await context.ScalarAsync<long>(
            $"SELECT count(*) FROM information_schema.tables WHERE table_schema = '{context.SchemaName}';");

        // 0001_initial: 13 tables + migration ledger. 0002_observability: 2 more
        // tables. 0003_agent_skills: 2 more tables. 0004_skill_scripts: 2 more
        // tables.
        // 0005_agent_call_graph ADDS NO NEW TABLE; it adds a column to the runs table.
        // 0006_attachments: 2 more tables (attachments, agent_files).
        // 0007_workflows: 2 more tables (workflows, workflow_checkpoints) and adds
        // the kind + workflow_name columns to the runs table.
        // 0008_scheduling: 3 more tables (job_schedules, jobs, job_items).
        // 0009_eval: 4 more tables (eval_suites, eval_cases, eval_runs, eval_case_results).
        // 0010_experiments: 1 more table (experiments); adds the agent_version,
        // experiment_id, variant columns to the runs table.
        // 0011_run_costs ADDS NO NEW TABLE; it adds cost columns to the runs table.
        // 0012_quotas_and_webhooks: 4 more tables (quotas, quota_usage,
        // webhook_subscriptions, webhook_deliveries) and adds the max_attempts
        // column to the jobs table.
        // 0013_mcp_oauth ADDS NO NEW TABLE; it adds a column to the mcp_servers table.
        // 0014_retention: 2 more tables (retention_policies, retention_runs).
        //
        // The number is DELIBERATELY a constant: adding a new table breaks this
        // test, so whoever adds it notices the table.
        // Phase 29 added the `voice_sessions` table: 38 -> 39.
        // Phase 31 added the `run_scores` table: 39 -> 40.
        // Phase 42 added the `singleton_leases` table: 40 -> 41.
        // Phase 43 added the `idempotency_keys` table: 41 -> 42.
        // Phase 47 added the `run_inputs` table: 42 -> 43.
        // Phase 51 added the `document_embeddings` table: 43 -> 44.
        // Phase 53 added the `api_keys` table: 44 -> 45.
        // Phase 55 added the `pending_approvals` table: 45 -> 46.
        // Phase 65 added `tenant_provider_bindings` and `tenant_egress_policies`: 46 -> 48.
        // Phase 66 added the `inbound_triggers` table: 48 -> 49.
        // Phase 67 relocated `document_embeddings` out of the core set into the
        // optional "knowledge" set; PostgresTestContext enables it by default
        // (applyMigrations: false + Migrations.ApplyAsync() below applies BOTH
        // sets), so the table still gets created here and the total is unchanged.
        tableCount.ShouldBe(49);
    }

    [Fact]
    public async Task Second_run_applies_nothing()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture, applyMigrations: false);

        (await context.Migrations.ApplyAsync()).ShouldBe(EmbeddedMigrationCount);
        (await context.Migrations.ApplyAsync()).ShouldBe(0);
        (await context.Migrations.ApplyAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Applied_migration_is_written_to_the_ledger()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        var name = await context.ScalarAsync<string>(
            $"SELECT name FROM {context.SchemaName}.__migrations WHERE set_name = 'core' ORDER BY id LIMIT 1;");

        name.ShouldBe("0001_initial");

        var checksum = await context.ScalarAsync<string>(
            $"SELECT checksum FROM {context.SchemaName}.__migrations WHERE set_name = 'core' ORDER BY id LIMIT 1;");

        checksum.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Modified_migration_throws()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        // Corrupts the ledger's checksum to simulate a modified file. set_name
        // is required: id 1 also exists in the "knowledge" set (0001_vector).
        await context.ExecuteAsync(
            $"UPDATE {context.SchemaName}.__migrations SET checksum = 'CORRUPTED' WHERE set_name = 'core' AND id = 1;");

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await context.Migrations.ApplyAsync());

        exception.Message.ShouldContain("0001_initial");
        exception.Message.ShouldContain("has changed");
    }

    [Fact]
    public async Task Five_concurrent_runs_apply_migrations_only_once()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        var contexts = new List<PostgresTestContext>();

        try
        {
            for (var i = 0; i < 5; i++)
            {
                contexts.Add(PostgresTestContext.Create(fixture, schemaName));
            }

            var results = await Task.WhenAll(
                contexts.Select(static context => context.Migrations.ApplyAsync().AsTask()));

            // Exactly one run applies the migrations; the others wait for it to
            // finish and find them already applied. pg_advisory_lock guarantees this.
            results.Count(count => count == EmbeddedMigrationCount).ShouldBe(1);
            results.Count(static count => count == 0).ShouldBe(4);

            var rowCount = await contexts[0].ScalarAsync<long>(
                $"SELECT count(*) FROM {schemaName}.__migrations;");

            rowCount.ShouldBe(EmbeddedMigrationCount);
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
    /// K-389: the migration lock is scoped to the schema. While schema A's lock
    /// is held, schema B's lock must be acquirable IMMEDIATELY; with a global
    /// lock, B would wait for the lock to be released.
    /// </summary>
    [Fact]
    public async Task Different_schemas_migration_locks_do_not_block_each_other()
    {
        var dialectA = new PostgresDialect(PostgresTestContext.NewSchemaName());
        var dialectB = new PostgresDialect(PostgresTestContext.NewSchemaName());

        await using var connectionA = new NpgsqlConnection(fixture.ConnectionString);
        await connectionA.OpenAsync();
        await dialectA.AcquireMigrationLockAsync(connectionA, 30, CancellationToken.None);

        try
        {
            await using var connectionB = new NpgsqlConnection(fixture.ConnectionString);
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
    public async Task Invalid_schema_name_is_rejected()
    {
        var options = Options.Create(new AgentPrismPostgreSqlOptions
        {
            ConnectionString = fixture.ConnectionString,
            SchemaName = "bad-name; DROP TABLE users",
        });

        await using var dataSource = new NpgsqlDataSourceBuilder(fixture.ConnectionString).Build();

        var exception = Should.Throw<AgentPrismException>(
            () => new MigrationRunner(
                new SqlStoreContext
                {
                    DataSource = dataSource,
                    Dialect = new PostgresDialect(options.Value.SchemaName),
                    CommandTimeoutSeconds = options.Value.CommandTimeoutSeconds,
                    ProviderName = "PostgreSQL",
                },
                NullLogger<MigrationRunner>.Instance));

        exception.Message.ShouldContain("schema name");
    }

    [Fact]
    public async Task Custom_schema_name_is_used()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = PostgresTestContext.Create(fixture, schemaName);

        await context.Migrations.ApplyAsync();

        var exists = await context.ScalarAsync<bool>(
            $"SELECT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = '{schemaName}');");

        exists.ShouldBeTrue();
    }
}
