using Tracon.Sqlite.IntegrationTests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>Behavior of the migration runner on SQLite.</summary>
public sealed class MigrationRunnerTests(SqliteFixture fixture)
{
    /// <summary>Number of embedded migrations.</summary>
    private static int EmbeddedMigrationCount { get; } = typeof(MigrationRunner).Assembly
        .GetManifestResourceNames()
        .Count(static name =>
            name.StartsWith("Tracon.Sqlite.Migrations.", StringComparison.Ordinal)
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

        var exception = await Should.ThrowAsync<TraconException>(
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
            Should.Throw<TraconException>(() => new SqliteDialect(invalid));
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

    /// <summary>
    /// Guards the index a score summary time-range query needs: without it,
    /// that query is a sequential scan of the whole table.
    /// </summary>
    [Fact]
    public async Task Score_summary_created_at_index_is_created()
    {
        var tablePrefix = SqliteTestContext.NewTablePrefix();

        await using var context = SqliteTestContext.Create(fixture, tablePrefix);
        await context.Migrations.ApplyAsync();

        var exists = await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = '{tablePrefix}run_scores_created_at_idx';");

        exists.ShouldBe(1);
    }

    [Fact]
    public void Migration_runner_reads_from_the_correct_assembly()
    {
        // The migration resource prefix is provider-specific; applying
        // PostgreSQL/SQL Server files to SQLite would silently build the
        // wrong schema.
        var dialect = new SqliteDialect("tracon_");

        dialect.MigrationResourcePrefix.ShouldBe("Tracon.Sqlite.Migrations.");
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
    /// SQLite cannot alter a primary key in place; <c>SqliteDialect</c>
    /// overrides <see cref="SqlDialect.UpgradeMigrationsTableAsync"/> to
    /// rebuild the table (the same technique as 0006_sessions_tenant_key.sql,
    /// K-278) — this test proves the rebuild preserves the pre-existing rows.
    /// </remarks>
    [Fact]
    public async Task Pre_phase_67_ledger_shape_upgrades_without_data_loss()
    {
        var tablePrefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, tablePrefix);

        var realCoreMigrations = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.Sqlite.Migrations.");

        await context.ExecuteAsync($"""
            CREATE TABLE {tablePrefix}__migrations (
                id         INTEGER NOT NULL PRIMARY KEY,
                name       TEXT    NOT NULL,
                checksum   TEXT    NOT NULL,
                applied_at TEXT    NOT NULL
            );
            """);

        foreach (var migration in realCoreMigrations)
        {
            await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteAsync(
                $"INSERT INTO {tablePrefix}__migrations (id, name, checksum, applied_at) " +
                $"VALUES ({migration.Id}, '{migration.Name}', '{migration.Checksum}', '2026-01-01T00:00:00.0000000Z');");
        }

        // Nothing left to apply; only the shape upgrade (rebuild) runs.
        (await context.Migrations.ApplyAsync()).ShouldBe(0);

        var coreRowCount = await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {tablePrefix}__migrations WHERE set_name = 'core';");

        coreRowCount.ShouldBe(realCoreMigrations.Count);
    }

    /// <summary>
    /// 🚨 The migration runner's bootstrap statements — the schema, the ledger,
    /// the ledger upgrade and the ledger read — ran OUTSIDE the transient-conflict
    /// retry that <c>ApplyOneAsync</c> already had. Measured on 2026-08-21: a
    /// batch run killed <c>SqlServerRunScoreStoreContractTests</c> whole (15 cases,
    /// 0 ms each) because <c>SqlDialect.UpgradeMigrationsTableAsync</c> was picked
    /// as a deadlock victim and the raw <c>SqlException</c> escaped
    /// <c>SqlServerSchemaFixture.InitializeAsync</c>.
    /// </summary>
    /// <remarks>
    /// The lock is scoped to the SCHEMA (K-389), so two schemas bootstrapping at
    /// the same time meet on catalog objects shared by the whole database. All
    /// four statements are idempotent — <c>IF NOT EXISTS</c>-guarded DDL and one
    /// SELECT — so retrying is as safe here as it is for a migration (K-540).
    /// </remarks>
    /// <param name="target">A fragment of the bootstrap statement to fail.</param>
    [Theory]
    [InlineData("SELECT 1;")]
    [InlineData("__migrations (")]
    [InlineData("pragma_table_info")]
    [InlineData("SELECT set_name, id, name, checksum")]
    public async Task Bootstrap_statement_is_retried_after_a_transient_conflict(string target)
    {
        var prefix = SqliteTestContext.NewTablePrefix();

        await using var owner = SqliteTestContext.Create(fixture, prefix);
        await using var faults = NewFaultDataSource(target, failures: 3);

        var applied = await NewRunner(faults, prefix).ApplyAsync(TestContext.Current.CancellationToken);

        applied.ShouldBe(EmbeddedMigrationCount);
        faults.ConsumedFailures.ShouldBe(3);
    }

    /// <summary>
    /// The retry is BOUNDED and its last attempt still produces a readable error.
    /// Before the fix the caller got a bare provider exception with no hint about
    /// which step failed.
    /// </summary>
    [Fact]
    public async Task Bootstrap_conflict_that_never_clears_is_reported_readably()
    {
        var prefix = SqliteTestContext.NewTablePrefix();

        await using var owner = SqliteTestContext.Create(fixture, prefix);
        await using var faults = NewFaultDataSource("__migrations (", failures: 1000);

        var runner = NewRunner(faults, prefix);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await runner.ApplyAsync(TestContext.Current.CancellationToken));

        // Eight attempts in total: seven are retried, the eighth is reported.
        faults.ConsumedFailures.ShouldBe(8);
        exception.Message.ShouldContain("migration ledger");
        exception.Message.ShouldContain("database is locked");
        exception.InnerException.ShouldBeOfType<SqliteException>();
    }

    private TransientFaultDataSource NewFaultDataSource(string target, int failures)
        => new(
            new SqliteDataSource(fixture.ConnectionString),
            sql => sql.Contains(target, StringComparison.Ordinal),
            failures);

    private static MigrationRunner NewRunner(TransientFaultDataSource dataSource, string tablePrefix)
        => new(
            new SqlStoreContext
            {
                DataSource = dataSource,
                Dialect = new SqliteDialect(tablePrefix),
                CommandTimeoutSeconds = 30,
                ProviderName = "SQLite",
            },
            NullLogger<MigrationRunner>.Instance);

    /// <summary>
    /// A database populated BEFORE phase 152 upgrades without losing a row: the
    /// judge rows keep their judge name, the human rows fall back to
    /// <c>overall</c>, and the widened <c>value</c> column stops rounding.
    /// </summary>
    /// <remarks>
    /// SQLite cannot alter a column in place, so 0035 adds <c>value_real</c>,
    /// copies, drops <c>value</c> and renames. This test is the only place that
    /// proves the copy carries the data across.
    /// </remarks>
    [Fact]
    public async Task Populated_pre_152_run_scores_upgrade_without_data_loss()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix);

        var migrations = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.Sqlite.Migrations.");

        // 🚨 Found BY NAME, not as migrations[^1]: the next phase that adds a
        // migration would otherwise turn this test red for an unrelated reason.
        var upgrade = migrations.Single(candidate =>
            string.Equals(candidate.Name, "0035_run_score_name_and_shape", StringComparison.Ordinal));

        await context.ExecuteAsync(context.StoreContext.Sql.CreateMigrationsTable);

        foreach (var migration in migrations.Where(candidate => candidate.Id < upgrade.Id))
        {
            await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteAsync(
                $"INSERT INTO {prefix}__migrations (set_name, id, name, checksum, applied_at) " +
                $"VALUES ('core', {migration.Id}, '{migration.Name}', '{migration.Checksum}', '2026-01-01T00:00:00.0000000Z');");
        }

        // 🚨 uuids are stored UPPERCASE in this dialect (K-191).
        var runId = TraconId.NewId().ToString("D").ToUpperInvariant();
        var humanId = TraconId.NewId().ToString("D").ToUpperInvariant();
        var judgeId = TraconId.NewId().ToString("D").ToUpperInvariant();

        await context.ExecuteAsync($"""
            INSERT INTO {prefix}run_scores
                (id, tenant_id, run_id, message_id, kind, value, comment, source, author, created_at)
            VALUES
                ('{humanId}', 'test', '{runId}', NULL, 1, 1, 'good', 'human', 'alice', '2026-01-01T00:00:00.0000000Z'),
                ('{judgeId}', 'test', '{runId}', NULL, 3, 70, 'ok', 'judge:quality', 'judge:quality', '2026-01-01T00:00:00.0000000Z');
            """);

        // Not hard-coded to 1: ApplyAsync() applies every pending migration,
        // not just `upgrade`, and a later phase can add one that also lands
        // after it (154 added an index migration right after this one).
        var pendingFromUpgrade = migrations.Count(candidate => candidate.Id >= upgrade.Id);
        (await context.Migrations.ApplyAsync()).ShouldBe(pendingFromUpgrade);

        var rows = await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {prefix}run_scores;");
        rows.ShouldBe(2);

        var humanName = await context.ScalarAsync<string>(
            $"SELECT name FROM {prefix}run_scores WHERE author = 'alice';");
        humanName.ShouldBe("overall");

        var judgeName = await context.ScalarAsync<string>(
            $"SELECT name FROM {prefix}run_scores WHERE author = 'judge:quality';");
        judgeName.ShouldBe("quality");

        // The widened column now accepts a decimal the old integer column
        // would have rounded away.
        await context.ExecuteAsync(
            $"UPDATE {prefix}run_scores SET value = 0.87 WHERE author = 'judge:quality';");

        (await context.ScalarAsync<double>(
            $"SELECT value FROM {prefix}run_scores WHERE author = 'judge:quality';")).ShouldBe(0.87);

        // The old uniqueness index is gone: the same author can now hold two names.
        await context.ExecuteAsync($"""
            INSERT INTO {prefix}run_scores
                (id, tenant_id, run_id, message_id, kind, value, comment, source, author, created_at, name, text_value)
            VALUES
                ('{TraconId.NewId().ToString("D").ToUpperInvariant()}', 'test', '{runId}', NULL, 1, 0, NULL, 'human', 'alice', '2026-01-01T00:00:00.0000000Z', 'accuracy', NULL);
            """);

        (await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {prefix}run_scores WHERE author = 'alice';")).ShouldBe(2);
    }

    [Fact]
    public void Migration_runner_cannot_be_constructed_with_an_invalid_prefix()
    {
        var exception = Should.Throw<TraconException>(() =>
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
