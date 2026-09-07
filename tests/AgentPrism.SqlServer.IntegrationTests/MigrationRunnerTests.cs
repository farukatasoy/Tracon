using AgentPrism.SqlServer.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.SqlServer.IntegrationTests;

/// <summary>The behavior of the migration runner on SQL Server.</summary>
public sealed class MigrationRunnerTests(SqlServerFixture fixture)
{
    /// <summary>The count of embedded migrations.</summary>
    private static int EmbeddedMigrationCount { get; } = typeof(MigrationRunner).Assembly
        .GetManifestResourceNames()
        .Count(static name =>
            name.StartsWith("AgentPrism.SqlServer.Migrations.", StringComparison.Ordinal)
            && name.EndsWith(".sql", StringComparison.Ordinal));

    [Fact]
    public async Task First_run_creates_the_schema_and_tables()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture, applyMigrations: false);

        var applied = await context.Migrations.ApplyAsync();

        applied.ShouldBe(EmbeddedMigrationCount);

        var tableCount = await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id " +
            $"WHERE s.name = '{context.SchemaName}';");

        // 0001_initial sets up PostgreSQL's 0001-0013 accumulation: 35 tables +
        // migration ledger = 36. 0002_retention adds 2 more tables (Phase 25):
        // retention_policies, retention_runs. The count should match the
        // PostgreSQL side; both providers carry the same data model.
        //
        // The number is DELIBERATELY a constant: adding a new table breaks this
        // test, so whoever adds it notices the table.
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
        await using var context = await SqlServerTestContext.CreateAsync(fixture, applyMigrations: false);

        (await context.Migrations.ApplyAsync()).ShouldBe(EmbeddedMigrationCount);
        (await context.Migrations.ApplyAsync()).ShouldBe(0);
        (await context.Migrations.ApplyAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Applied_migration_is_written_to_the_ledger()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        var recorded = await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM {context.SchemaName}.__migrations;");

        recorded.ShouldBe(EmbeddedMigrationCount);

        var checksum = await context.ScalarAsync<string>(
            $"SELECT checksum FROM {context.SchemaName}.__migrations WHERE id = 1;");

        checksum.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Modified_migration_throws()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        // Corrupting the ledger's checksum has the same effect as modifying the file.
        await context.ExecuteAsync(
            $"UPDATE {context.SchemaName}.__migrations SET checksum = 'CORRUPTED' WHERE id = 1;");

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await context.Migrations.ApplyAsync());

        exception.Message.ShouldContain("has changed");
    }

    /// <summary>
    /// Five concurrent runners serialize through <c>sp_getapplock</c>, and the
    /// migrations are applied only once in total.
    /// </summary>
    [Fact]
    public async Task Five_concurrent_runs_apply_migrations_only_once()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();
        var contexts = new List<SqlServerTestContext>();

        try
        {
            for (var index = 0; index < 5; index++)
            {
                contexts.Add(SqlServerTestContext.Create(fixture, schemaName));
            }

            var results = await Task.WhenAll(contexts.Select(static async context =>
                await context.Migrations.ApplyAsync().AsTask()));

            // Exactly one runner applies them; the others wait for the lock and
            // find the ledger already full.
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
    /// K-389: the migration lock is scoped to the schema. While schema A's
    /// lock is held, schema B's lock must be acquirable IMMEDIATELY; with a
    /// global lock, B would wait up to <c>LockTimeoutMilliseconds</c> (30 s).
    /// </summary>
    [Fact]
    public async Task Different_schemas_migration_locks_do_not_block_each_other()
    {
        var dialectA = new SqlServerDialect(SqlServerTestContext.NewSchemaName());
        var dialectB = new SqlServerDialect(SqlServerTestContext.NewSchemaName());

        await using var connectionA = new SqlConnection(fixture.ConnectionString);
        await connectionA.OpenAsync();
        await dialectA.AcquireMigrationLockAsync(connectionA, 30, CancellationToken.None);

        try
        {
            await using var connectionB = new SqlConnection(fixture.ConnectionString);
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
    public void Invalid_schema_name_is_rejected()
    {
        // Uppercase, quotes, and a period are rejected: the schema name is
        // embedded directly into SQL text, and the injection surface is closed
        // here (K-029).
        foreach (var invalid in new[] { "Agent", "agent-prism", "agent.prism", "agent prism", "dbo';--" })
        {
            Should.Throw<AgentPrismException>(() => new SqlServerDialect(invalid));
        }
    }

    [Fact]
    public async Task Custom_schema_name_is_used()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();

        await using var context = SqlServerTestContext.Create(fixture, schemaName);
        await context.Migrations.ApplyAsync();

        var exists = await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM sys.schemas WHERE name = '{schemaName}';");

        exists.ShouldBe(1);
    }

    /// <summary>
    /// Guards the index a score summary time-range query needs: without it,
    /// that query is a sequential scan of the whole table.
    /// </summary>
    [Fact]
    public async Task Score_summary_created_at_index_is_created()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();

        await using var context = SqlServerTestContext.Create(fixture, schemaName);
        await context.Migrations.ApplyAsync();

        var exists = await context.ScalarAsync<int>($"""
            SELECT COUNT(*) FROM sys.indexes
            WHERE name = 'run_scores_created_at_idx'
              AND object_id = OBJECT_ID(N'{schemaName}.run_scores');
            """);

        exists.ShouldBe(1);
    }

    [Fact]
    public void Migration_runner_reads_from_the_correct_assembly()
    {
        // The migration resource prefix is provider-specific; applying
        // PostgreSQL's files to SQL Server would silently set up the wrong schema.
        var dialect = new SqlServerDialect("agentprism");

        dialect.MigrationResourcePrefix.ShouldBe("AgentPrism.SqlServer.Migrations.");
        MigrationDescriptor
            .Discover(dialect.GetType().Assembly, dialect.MigrationResourcePrefix)
            .Count.ShouldBe(EmbeddedMigrationCount);
    }

    /// <summary>
    /// A database that already applied the core set under the shape that
    /// existed before phase 67 (id-only ledger primary key, no
    /// <c>set_name</c> column — simulated here with raw DDL) upgrades safely.
    /// </summary>
    /// <remarks>
    /// This is the specific scenario that requires
    /// <see cref="SqlDialect.UpgradeMigrationsTableAsync"/> to run as its OWN
    /// command, never combined with a migration's <c>InsertMigration</c>
    /// text: SQL Server compiles a whole batch up front and a statement
    /// referencing a column added earlier IN THE SAME BATCH via plain
    /// <c>ALTER TABLE</c> fails with "Invalid column name" — this test would
    /// catch a regression back to the combined-batch approach.
    /// </remarks>
    [Fact]
    public async Task Pre_phase_67_ledger_shape_upgrades_without_data_loss()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();
        await using var context = SqlServerTestContext.Create(fixture, schemaName);

        var realCoreMigrations = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "AgentPrism.SqlServer.Migrations.");

        await context.ExecuteAsync($"CREATE SCHEMA {schemaName};");
        await context.ExecuteAsync($"""
            CREATE TABLE {schemaName}.__migrations (
                id         int               NOT NULL CONSTRAINT __migrations_pk PRIMARY KEY,
                name       nvarchar(200)     NOT NULL,
                checksum   nvarchar(64)      NOT NULL,
                applied_at datetimeoffset(7) NOT NULL
            );
            """);

        foreach (var migration in realCoreMigrations)
        {
            await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteAsync(
                $"INSERT INTO {schemaName}.__migrations (id, name, checksum, applied_at) " +
                $"VALUES ({migration.Id}, N'{migration.Name}', N'{migration.Checksum}', SYSDATETIMEOFFSET());");
        }

        // Nothing left to apply; only the shape upgrade runs.
        (await context.Migrations.ApplyAsync()).ShouldBe(0);

        var coreRowCount = await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM {schemaName}.__migrations WHERE set_name = N'core';");

        coreRowCount.ShouldBe(realCoreMigrations.Count);
    }

    /// <summary>
    /// A database populated BEFORE phase 152 upgrades without losing a row: the
    /// judge rows keep their judge name, the human rows fall back to
    /// <c>overall</c>, the widened <c>value</c> column stops rounding, and the
    /// old FILTERED uniqueness index no longer blocks a second name per author.
    /// </summary>
    [Fact]
    public async Task Populated_pre_152_run_scores_upgrade_without_data_loss()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();
        await using var context = SqlServerTestContext.Create(fixture, schemaName);

        var migrations = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "AgentPrism.SqlServer.Migrations.");

        // 🚨 Found BY NAME, not as migrations[^1]: the next phase that adds a
        // migration would otherwise turn this test red for an unrelated reason.
        var upgrade = migrations.Single(candidate =>
            string.Equals(candidate.Name, "0035_run_score_name_and_shape", StringComparison.Ordinal));

        await context.ExecuteAsync($"IF SCHEMA_ID(N'{schemaName}') IS NULL EXEC(N'CREATE SCHEMA {schemaName};');");
        await context.ExecuteAsync(context.StoreContext.Sql.CreateMigrationsTable);

        foreach (var migration in migrations.Where(candidate => candidate.Id < upgrade.Id))
        {
            await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteAsync(
                $"INSERT INTO {schemaName}.__migrations (set_name, id, name, checksum, applied_at) " +
                $"VALUES (N'core', {migration.Id}, N'{migration.Name}', N'{migration.Checksum}', '2026-01-01T00:00:00Z');");
        }

        var runId = AgentPrismId.NewId();

        await context.ExecuteAsync($"""
            INSERT INTO {schemaName}.run_scores
                (id, tenant_id, run_id, message_id, kind, value, comment, source, author, created_at)
            VALUES
                ('{AgentPrismId.NewId()}', N'test', '{runId}', NULL, 1, 1, N'good', N'human', N'alice', '2026-01-01T00:00:00Z'),
                ('{AgentPrismId.NewId()}', N'test', '{runId}', NULL, 3, 70, N'ok', N'judge:quality', N'judge:quality', '2026-01-01T00:00:00Z');
            """);

        // Not hard-coded to 1: ApplyAsync() applies every pending migration,
        // not just `upgrade`, and a later phase can add one that also lands
        // after it (154 added an index migration right after this one).
        var pendingFromUpgrade = migrations.Count(candidate => candidate.Id >= upgrade.Id);
        (await context.Migrations.ApplyAsync()).ShouldBe(pendingFromUpgrade);

        (await context.ScalarAsync<int>($"SELECT COUNT(*) FROM {schemaName}.run_scores;")).ShouldBe(2);

        (await context.ScalarAsync<string>(
            $"SELECT name FROM {schemaName}.run_scores WHERE author = N'alice';")).ShouldBe("overall");

        (await context.ScalarAsync<string>(
            $"SELECT name FROM {schemaName}.run_scores WHERE author = N'judge:quality';")).ShouldBe("quality");

        await context.ExecuteAsync(
            $"UPDATE {schemaName}.run_scores SET value = 0.87 WHERE author = N'judge:quality';");

        (await context.ScalarAsync<double>(
            $"SELECT value FROM {schemaName}.run_scores WHERE author = N'judge:quality';")).ShouldBe(0.87);

        await context.ExecuteAsync($"""
            INSERT INTO {schemaName}.run_scores
                (id, tenant_id, run_id, message_id, kind, value, comment, source, author, created_at, name, text_value)
            VALUES
                ('{AgentPrismId.NewId()}', N'test', '{runId}', NULL, 1, 0, NULL, N'human', N'alice', '2026-01-01T00:00:00Z', N'accuracy', NULL);
            """);

        (await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM {schemaName}.run_scores WHERE author = N'alice';")).ShouldBe(2);
    }

    [Fact]
    public void Migration_runner_cannot_be_constructed_with_an_invalid_schema()
    {
        var exception = Should.Throw<AgentPrismException>(() =>
            new MigrationRunner(
                new SqlStoreContext
                {
                    DataSource = new SqlServerDataSource(fixture.ConnectionString),
                    Dialect = new SqlServerDialect("PUBLIC"),
                    CommandTimeoutSeconds = 30,
                    ProviderName = "SQL Server",
                },
                NullLogger<MigrationRunner>.Instance));

        exception.Message.ShouldContain("schema name");
    }
}
