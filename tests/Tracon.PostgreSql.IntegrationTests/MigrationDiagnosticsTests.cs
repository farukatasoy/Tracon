using Tracon.PostgreSql.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// Verifies <see cref="MigrationRunner"/>'s <see cref="ISqlPersistenceDiagnostics"/>
/// implementation against a real PostgreSQL container (Phase 33).
/// </summary>
/// <remarks>
/// <c>GetSnapshotAsync</c> does NOT APPLY any migration — so each test calls
/// <c>ApplyAsync</c> as a separate verification step and compares before/after.
/// </remarks>
public sealed class MigrationDiagnosticsTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Provider_name_returns_PostgreSQL()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture, applyMigrations: false);

        ((ISqlPersistenceDiagnostics)context.Migrations).ProviderName.ShouldBe("PostgreSQL");
    }

    [Fact]
    public async Task Before_migration_is_applied_pending_is_full_and_connection_works()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture, applyMigrations: false);

        var snapshot = await context.Migrations.GetSnapshotAsync();

        snapshot.CanConnect.ShouldBeTrue();
        snapshot.PendingMigrations.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task After_migration_is_applied_no_pending_remains()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture, applyMigrations: false);

        await context.Migrations.ApplyAsync();
        var snapshot = await context.Migrations.GetSnapshotAsync();

        snapshot.CanConnect.ShouldBeTrue();
        snapshot.PendingMigrations.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetSnapshotAsync_does_NOT_APPLY_migrations()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture, applyMigrations: false);

        // Calling the same probe twice should NOT CHANGE the schema state.
        await context.Migrations.GetSnapshotAsync();
        var snapshot = await context.Migrations.GetSnapshotAsync();

        snapshot.PendingMigrations.ShouldNotBeEmpty();

        var migrationsTableExists = await context.ScalarAsync<bool>(
            $"SELECT EXISTS (SELECT 1 FROM information_schema.tables " +
            $"WHERE table_schema = '{context.SchemaName}' AND table_name = '__migrations');");

        migrationsTableExists.ShouldBeFalse();
    }

    [Fact]
    public async Task Unreachable_provider_returns_CanConnect_false()
    {
        var options = new TraconPostgreSqlOptions
        {
            // A closed/reserved port: produces a real connection refusal instead
            // of waiting for a network timeout (Timeout is lowered to 2 seconds).
            ConnectionString = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2",
            SchemaName = PostgresTestContext.NewSchemaName(),
            AutoApplyMigrations = false,
            CommandTimeoutSeconds = 5,
        };

        await using var dataSource = new Npgsql.NpgsqlDataSourceBuilder(options.ConnectionString).Build();

        var context = new SqlStoreContext
        {
            DataSource = dataSource,
            Dialect = new PostgresDialect(options.SchemaName),
            CommandTimeoutSeconds = options.CommandTimeoutSeconds,
            ProviderName = "PostgreSQL",
        };

        var runner = new MigrationRunner(context, NullLogger<MigrationRunner>.Instance);

        var snapshot = await runner.GetSnapshotAsync();

        snapshot.CanConnect.ShouldBeFalse();
        snapshot.PendingMigrations.ShouldBeEmpty();
    }
}
