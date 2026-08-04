using AgentPrism.SqlServer.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.SqlServer.IntegrationTests;

/// <summary>Migration calistiricisinin SQL Server uzerindeki davranisi.</summary>
public sealed class MigrationRunnerTests(SqlServerFixture fixture)
{
    /// <summary>Gomulu migration sayisi.</summary>
    private static int EmbeddedMigrationCount { get; } = typeof(MigrationRunner).Assembly
        .GetManifestResourceNames()
        .Count(static name =>
            name.StartsWith("AgentPrism.SqlServer.Migrations.", StringComparison.Ordinal)
            && name.EndsWith(".sql", StringComparison.Ordinal));

    [Fact]
    public async Task Ilk_kosuda_sema_ve_tablolar_olusur()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture, applyMigrations: false);

        var applied = await context.Migrations.ApplyAsync();

        applied.ShouldBe(EmbeddedMigrationCount);

        var tableCount = await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id " +
            $"WHERE s.name = '{context.SchemaName}';");

        // 0001_initial PostgreSQL'in 0001-0013 birikimini kurar: 35 tablo +
        // migration defteri = 36. Sayi PostgreSQL tarafiyla ayni olmalidir; iki
        // saglayici ayni veri modelini tasir.
        //
        // Sayi BILEREK sabittir: yeni bir tablo eklendiginde bu test kirilir ve
        // ekleyen kisi tabloyu fark etmis olur.
        tableCount.ShouldBe(36);
    }

    [Fact]
    public async Task Ikinci_kosu_hicbir_sey_uygulamaz()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture, applyMigrations: false);

        (await context.Migrations.ApplyAsync()).ShouldBe(EmbeddedMigrationCount);
        (await context.Migrations.ApplyAsync()).ShouldBe(0);
        (await context.Migrations.ApplyAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Uygulanan_migration_deftere_yazilir()
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
    public async Task Degistirilmis_migration_hata_verir()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        // Defterdeki ozeti bozmak, dosyanin degistirilmesiyle ayni sonucu verir.
        await context.ExecuteAsync(
            $"UPDATE {context.SchemaName}.__migrations SET checksum = 'BOZUK' WHERE id = 1;");

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await context.Migrations.ApplyAsync());

        exception.Message.ShouldContain("icerigi degismis");
    }

    /// <summary>
    /// Bes es zamanli calistirici <c>sp_getapplock</c> ile sirayla gecer ve
    /// migration'lar toplamda yalnizca bir kez uygulanir.
    /// </summary>
    [Fact]
    public async Task Bes_es_zamanli_kosuda_migration_tek_kez_uygulanir()
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

            // Tam olarak bir calistirici uygular; digerleri kilidi bekler ve
            // defteri dolu bulur.
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

    [Fact]
    public void Gecersiz_sema_adi_reddedilir()
    {
        // Buyuk harf, tirnak ve nokta reddedilir: sema adi SQL metnine dogrudan
        // gomulur ve enjeksiyon yuzeyi burada kapanir (K-029).
        foreach (var invalid in new[] { "Agent", "agent-prism", "agent.prism", "agent prism", "dbo';--" })
        {
            Should.Throw<AgentPrismException>(() => new SqlServerDialect(invalid));
        }
    }

    [Fact]
    public async Task Ozel_sema_adi_kullanilir()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();

        await using var context = SqlServerTestContext.Create(fixture, schemaName);
        await context.Migrations.ApplyAsync();

        var exists = await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM sys.schemas WHERE name = '{schemaName}';");

        exists.ShouldBe(1);
    }

    [Fact]
    public void Migration_calistiricisi_dogru_derlemeden_okur()
    {
        // Migration kaynak oneki saglayiciya ozgudur; PostgreSQL'in dosyalarini
        // SQL Server'a uygulamak sessizce yanlis sema kurardi.
        var dialect = new SqlServerDialect("agentprism");

        dialect.MigrationResourcePrefix.ShouldBe("AgentPrism.SqlServer.Migrations.");
        MigrationDescriptor
            .Discover(dialect.GetType().Assembly, dialect.MigrationResourcePrefix)
            .Count.ShouldBe(EmbeddedMigrationCount);
    }

    [Fact]
    public void Migration_calistiricisi_gecersiz_sema_ile_kurulamaz()
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

        exception.Message.ShouldContain("sema adi");
    }
}
