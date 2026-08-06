using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// <see cref="MigrationRunner"/>'in <see cref="ISqlPersistenceDiagnostics"/> uygulamasini
/// gercek bir PostgreSQL container'ina karsi dogrular (Faz 33).
/// </summary>
/// <remarks>
/// <c>GetSnapshotAsync</c> hicbir migration UYGULAMAZ — bu yuzden her testte ayri bir
/// dogrulama adimi olarak <c>ApplyAsync</c> cagrilir ve oncesi/sonrasi karsilastirilir.
/// </remarks>
public sealed class MigrationDiagnosticsTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Saglayici_adi_PostgreSQL_doner()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture, applyMigrations: false);

        ((ISqlPersistenceDiagnostics)context.Migrations).ProviderName.ShouldBe("PostgreSQL");
    }

    [Fact]
    public async Task Migration_uygulanmadan_once_bekleyenler_dolu_ve_baglanti_calisir()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture, applyMigrations: false);

        var snapshot = await context.Migrations.GetSnapshotAsync();

        snapshot.CanConnect.ShouldBeTrue();
        snapshot.PendingMigrations.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Migration_uygulandiktan_sonra_bekleyen_kalmaz()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture, applyMigrations: false);

        await context.Migrations.ApplyAsync();
        var snapshot = await context.Migrations.GetSnapshotAsync();

        snapshot.CanConnect.ShouldBeTrue();
        snapshot.PendingMigrations.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetSnapshotAsync_migration_UYGULAMAZ()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture, applyMigrations: false);

        // Ayni sinamayi iki kez cagirmak sema durumunu DEGISTIRMEMELIDIR.
        await context.Migrations.GetSnapshotAsync();
        var snapshot = await context.Migrations.GetSnapshotAsync();

        snapshot.PendingMigrations.ShouldNotBeEmpty();

        var migrationsTableExists = await context.ScalarAsync<bool>(
            $"SELECT EXISTS (SELECT 1 FROM information_schema.tables " +
            $"WHERE table_schema = '{context.SchemaName}' AND table_name = '__migrations');");

        migrationsTableExists.ShouldBeFalse();
    }

    [Fact]
    public async Task Baglanti_kurulamayan_saglayici_CanConnect_false_doner()
    {
        var options = new AgentPrismPostgreSqlOptions
        {
            // Kapali/ayrilmis bir port: gercek bir baglanti reddi uretir, ag zaman
            // asimi beklemez (Timeout=2 saniyeye dusurur).
            ConnectionString = "Host=127.0.0.1;Port=1;Database=yok;Username=yok;Password=yok;Timeout=2",
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
