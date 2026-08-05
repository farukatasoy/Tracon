using AgentPrism.Sqlite.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Sqlite.IntegrationTests;

/// <summary>Migration calistiricisinin SQLite uzerindeki davranisi.</summary>
public sealed class MigrationRunnerTests(SqliteFixture fixture)
{
    /// <summary>Gomulu migration sayisi.</summary>
    private static int EmbeddedMigrationCount { get; } = typeof(MigrationRunner).Assembly
        .GetManifestResourceNames()
        .Count(static name =>
            name.StartsWith("AgentPrism.Sqlite.Migrations.", StringComparison.Ordinal)
            && name.EndsWith(".sql", StringComparison.Ordinal));

    [Fact]
    public async Task Ilk_kosuda_tablolar_olusur()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture, applyMigrations: false);

        var applied = await context.Migrations.ApplyAsync();

        applied.ShouldBe(EmbeddedMigrationCount);

        var tableCount = await context.ScalarAsync<long>($"""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table' AND name LIKE '{context.TablePrefix}%' ESCAPE '\';
            """);

        // 0001_initial PostgreSQL'in 0001-0013 birikimini kurar: 35 tablo +
        // migration defteri = 36. 0002_retention 2 tablo daha ekler (Faz 25):
        // retention_policies, retention_runs. Sayi diger saglayicilarla AYNI
        // olmalidir; ucu de ayni veri modelini tasir. Sayi BILEREK sabittir:
        // yeni bir tablo eklendiginde bu test kirilir ve ekleyen kisi tabloyu
        // fark eder.
        // Faz 29 `voice_sessions` tablosunu ekledi: 38 -> 39.
        tableCount.ShouldBe(39);
    }

    [Fact]
    public async Task Ikinci_kosu_hicbir_sey_uygulamaz()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture, applyMigrations: false);

        (await context.Migrations.ApplyAsync()).ShouldBe(EmbeddedMigrationCount);
        (await context.Migrations.ApplyAsync()).ShouldBe(0);
        (await context.Migrations.ApplyAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Uygulanan_migration_deftere_yazilir()
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
    public async Task Degistirilmis_migration_hata_verir()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

        // Defterdeki ozeti bozmak, dosyanin degistirilmesiyle ayni sonucu verir.
        await context.ExecuteAsync(
            $"UPDATE {context.TablePrefix}__migrations SET checksum = 'BOZUK' WHERE id = 1;");

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await context.Migrations.ApplyAsync());

        exception.Message.ShouldContain("icerigi degismis");
    }

    /// <summary>
    /// Bes es zamanli calistirici sidecar dosya kilidiyle sirayla gecer ve
    /// migration'lar toplamda yalnizca bir kez uygulanir. Ayni veritabani
    /// dosyasini (fixture) paylasan ama ayri tablo onekleri kullanan bes
    /// baglam, ayni kilit dosyasi icin yarisir.
    /// </summary>
    [Fact]
    public async Task Bes_es_zamanli_kosuda_migration_tek_kez_uygulanir()
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
    public void Gecersiz_tablo_oneki_reddedilir()
    {
        // Buyuk harf, tirnak ve nokta reddedilir: onek SQL metnine dogrudan
        // gomulur ve enjeksiyon yuzeyi burada kapanir (K-029'un SQLite karsiligi).
        foreach (var invalid in new[] { "Agent", "agent-prism", "agent.prism", "agent prism", "dbo';--" })
        {
            Should.Throw<AgentPrismException>(() => new SqliteDialect(invalid));
        }
    }

    [Fact]
    public async Task Ozel_tablo_oneki_kullanilir()
    {
        var tablePrefix = SqliteTestContext.NewTablePrefix();

        await using var context = SqliteTestContext.Create(fixture, tablePrefix);
        await context.Migrations.ApplyAsync();

        var exists = await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{tablePrefix}tenants';");

        exists.ShouldBe(1);
    }

    [Fact]
    public void Migration_calistiricisi_dogru_derlemeden_okur()
    {
        // Migration kaynak oneki saglayiciya ozgudur; PostgreSQL/SQL Server'in
        // dosyalarini SQLite'a uygulamak sessizce yanlis sema kurardi.
        var dialect = new SqliteDialect("agentprism_");

        dialect.MigrationResourcePrefix.ShouldBe("AgentPrism.Sqlite.Migrations.");
        MigrationDescriptor
            .Discover(dialect.GetType().Assembly, dialect.MigrationResourcePrefix)
            .Count.ShouldBe(EmbeddedMigrationCount);
    }

    [Fact]
    public void Migration_calistiricisi_gecersiz_onekle_kurulamaz()
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

        exception.Message.ShouldContain("sema adi");
    }
}
