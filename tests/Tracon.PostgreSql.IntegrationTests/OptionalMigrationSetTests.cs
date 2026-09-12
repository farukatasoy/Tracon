using Tracon.PostgreSql.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// The "knowledge" optional migration set (phase 67): opt-in, needs
/// <c>pgvector</c>, numbers its own files independently from the core set.
/// </summary>
public sealed class OptionalMigrationSetTests(PostgresFixture fixture)
{
    /// <summary>
    /// <c>GetSnapshotAsync</c> must not report a disabled set's files as
    /// pending — the exact failure mode 67.2's "reject the file-name-skip
    /// alternative" rationale warns about (a permanently-Degraded diagnostic).
    /// </summary>
    [Fact]
    public async Task GetSnapshotAsync_does_not_count_a_disabled_sets_files_as_pending()
    {
        await using var disabled = await PostgresTestContext.CreateAsync(
            fixture,
            applyMigrations: false,
            enableKnowledge: false);

        var snapshotDisabled = await disabled.Migrations.GetSnapshotAsync();

        snapshotDisabled.PendingMigrations.ShouldNotBeEmpty();
        snapshotDisabled.PendingMigrations.ShouldNotContain(static name => name.StartsWith("knowledge:", StringComparison.Ordinal));

        await using var enabled = PostgresTestContext.Create(
            fixture,
            disabled.SchemaName + "_x",
            enableKnowledge: true);

        var snapshotEnabled = await enabled.Migrations.GetSnapshotAsync();

        snapshotEnabled.PendingMigrations.ShouldContain(static name => string.Equals(name, "knowledge:0001_vector", StringComparison.Ordinal));
    }

    /// <summary>
    /// A core-numbered ledger row whose file relocated to a disabled optional
    /// set is logged once at information level (decision 67.3 — a
    /// diagnostic, not migration code); it must not throw or block startup.
    /// </summary>
    [Fact]
    public async Task Orphaned_relocated_core_row_is_logged_and_harmless()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = PostgresTestContext.Create(fixture, schemaName, enableKnowledge: false);

        await context.ExecuteAsync($"CREATE SCHEMA {schemaName};");
        await context.ExecuteAsync($"""
            CREATE TABLE {schemaName}.__migrations (
                set_name   text        NOT NULL DEFAULT 'core',
                id         integer     NOT NULL,
                name       text        NOT NULL,
                checksum   text        NOT NULL,
                applied_at timestamptz NOT NULL,
                PRIMARY KEY (set_name, id)
            );
            """);
        await context.ExecuteAsync($"""
            INSERT INTO {schemaName}.__migrations (set_name, id, name, checksum, applied_at)
            VALUES ('core', 24, '0024_vector', 'legacy-checksum-no-longer-discoverable', now());
            """);

        var logger = new CapturingLogger<MigrationRunner>();

        var runner = new MigrationRunner(context.StoreContext, logger);

        await Should.NotThrowAsync(async () => await runner.ApplyAsync());

        logger.InformationMessages.ShouldContain(message =>
            message.Contains("24", StringComparison.Ordinal) && message.Contains("knowledge", StringComparison.Ordinal));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> InformationMessages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Information)
            {
                InformationMessages.Add(formatter(state, exception));
            }
        }
    }

    [Fact]
    public async Task Knowledge_disabled_by_default_creates_no_vector_objects()
    {
        await using var context = await PostgresTestContext.CreateAsync(
            fixture,
            applyMigrations: false,
            enableKnowledge: false);

        await context.Migrations.ApplyAsync();

        var hasDocumentEmbeddings = await context.ScalarAsync<bool>(
            $"SELECT EXISTS (SELECT 1 FROM information_schema.tables " +
            $"WHERE table_schema = '{context.SchemaName}' AND table_name = 'document_embeddings');");

        hasDocumentEmbeddings.ShouldBeFalse();

        var ledgerHasKnowledgeRows = await context.ScalarAsync<long>(
            $"SELECT count(*) FROM {context.SchemaName}.__migrations WHERE set_name = 'knowledge';");

        ledgerHasKnowledgeRows.ShouldBe(0);
    }

    [Fact]
    public async Task Knowledge_enabled_creates_document_embeddings_and_the_extension()
    {
        await using var context = await PostgresTestContext.CreateAsync(
            fixture,
            applyMigrations: false,
            enableKnowledge: true);

        await context.Migrations.ApplyAsync();

        var hasDocumentEmbeddings = await context.ScalarAsync<bool>(
            $"SELECT EXISTS (SELECT 1 FROM information_schema.tables " +
            $"WHERE table_schema = '{context.SchemaName}' AND table_name = 'document_embeddings');");

        hasDocumentEmbeddings.ShouldBeTrue();

        var ledgerRow = await context.ScalarAsync<string>(
            $"SELECT name FROM {context.SchemaName}.__migrations WHERE set_name = 'knowledge' AND id = 1;");

        ledgerRow.ShouldBe("0001_vector");
    }

    /// <summary>
    /// The core set's <c>0001_initial</c> and the knowledge set's
    /// <c>0001_vector</c> both use id 1; the ledger's primary key is
    /// <c>(set_name, id)</c>, not <c>id</c> alone, so both rows coexist.
    /// </summary>
    [Fact]
    public async Task Two_sets_numbering_from_one_do_not_collide_in_the_ledger()
    {
        await using var context = await PostgresTestContext.CreateAsync(
            fixture,
            applyMigrations: false,
            enableKnowledge: true);

        await context.Migrations.ApplyAsync();

        var idOneRowCount = await context.ScalarAsync<long>(
            $"SELECT count(*) FROM {context.SchemaName}.__migrations WHERE id = 1;");

        idOneRowCount.ShouldBe(2);

        var names = new List<string>();

        await using (var command = context.DataSource.CreateCommand(
            $"SELECT name FROM {context.SchemaName}.__migrations WHERE id = 1 ORDER BY set_name;"))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(0));
            }
        }

        names.ShouldBe(["0001_initial", "0001_vector"]);
    }

    /// <summary>
    /// A schema first migrated with knowledge OFF, then restarted with it ON
    /// (manual acceptance case 4): only the knowledge set applies; the core
    /// set does not run again.
    /// </summary>
    [Fact]
    public async Task Enabling_knowledge_later_applies_only_the_new_set()
    {
        var schemaName = PostgresTestContext.NewSchemaName();

        await using (var first = PostgresTestContext.Create(fixture, schemaName, enableKnowledge: false))
        {
            (await first.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);
        }

        await using var second = PostgresTestContext.Create(fixture, schemaName, enableKnowledge: true);

        var appliedOnSecondRun = await second.Migrations.ApplyAsync();

        // Only the knowledge set's single migration is new; the core set is
        // already fully applied and does not run again.
        appliedOnSecondRun.ShouldBe(1);

        var hasDocumentEmbeddings = await second.ScalarAsync<bool>(
            $"SELECT EXISTS (SELECT 1 FROM information_schema.tables " +
            $"WHERE table_schema = '{schemaName}' AND table_name = 'document_embeddings');");

        hasDocumentEmbeddings.ShouldBeTrue();
    }

    [Fact]
    public async Task Unknown_migration_set_name_throws_at_startup()
    {
        await using var dataSource = new Npgsql.NpgsqlDataSourceBuilder(fixture.ConnectionString).Build();

        var runner = new MigrationRunner(
            new SqlStoreContext
            {
                DataSource = dataSource,
                Dialect = new PostgresDialect(PostgresTestContext.NewSchemaName()),
                CommandTimeoutSeconds = 30,
                ProviderName = "PostgreSQL",
                EnabledMigrationSets = new HashSet<string>(StringComparer.Ordinal) { "not-a-real-set" },
            },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MigrationRunner>.Instance);

        var exception = await Should.ThrowAsync<TraconException>(async () => await runner.ApplyAsync());

        exception.Message.ShouldContain("not-a-real-set");
    }

    /// <summary>
    /// A database that already applied the core set under the shape that
    /// existed before phase 67 (id-only ledger primary key, no
    /// <c>set_name</c> column — simulated here with raw DDL) upgrades safely:
    /// the pre-existing row is backfilled as <c>core</c>, and the knowledge
    /// set (also numbering from 1) can apply afterward without a primary key
    /// collision.
    /// </summary>
    [Fact]
    public async Task Pre_phase_67_ledger_shape_upgrades_without_data_loss()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = PostgresTestContext.Create(fixture, schemaName, enableKnowledge: false);

        var realCoreMigrations = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.PostgreSql.Migrations.");

        // Replicates a genuine pre-phase-67 database: every core migration's
        // real DDL actually ran (so later migrations that reference earlier
        // tables/columns keep working), recorded in the OLD ledger shape
        // (id-only primary key, no set_name column).
        await context.ExecuteAsync($"CREATE SCHEMA {schemaName};");
        await context.ExecuteAsync($"""
            CREATE TABLE {schemaName}.__migrations (
                id         integer     NOT NULL PRIMARY KEY,
                name       text        NOT NULL,
                checksum   text        NOT NULL,
                applied_at timestamptz NOT NULL
            );
            """);

        foreach (var migration in realCoreMigrations)
        {
            await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteAsync($"""
                INSERT INTO {schemaName}.__migrations (id, name, checksum, applied_at)
                VALUES ({migration.Id}, '{migration.Name}', '{migration.Checksum}', now());
                """);
        }

        // Nothing left to apply for the core set; only the shape upgrade runs.
        (await context.Migrations.ApplyAsync()).ShouldBe(0);

        var coreRowCount = await context.ScalarAsync<long>(
            $"SELECT count(*) FROM {schemaName}.__migrations WHERE set_name = 'core';");

        coreRowCount.ShouldBe(realCoreMigrations.Count);

        // The composite primary key is really in place: the knowledge set's
        // id 1 does not collide with the core set's id 1.
        await using var withKnowledge = PostgresTestContext.Create(fixture, schemaName, enableKnowledge: true);

        await Should.NotThrowAsync(async () => await withKnowledge.Migrations.ApplyAsync());

        var idOneRowCount = await context.ScalarAsync<long>(
            $"SELECT count(*) FROM {schemaName}.__migrations WHERE id = 1;");

        idOneRowCount.ShouldBe(2);
    }
}
