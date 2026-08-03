using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>Migration calistiricisinin davranisi.</summary>
public sealed class MigrationRunnerTests(PostgresFixture fixture)
{
    /// <summary>
    /// Gomulu migration sayisi. Sabit yazilmaz: her yeni migration dosyasi bu
    /// testleri kirardi ve kirilma, testin dogruladigi davranisla ilgisiz olurdu.
    /// </summary>
    private static int EmbeddedMigrationCount { get; } = typeof(MigrationRunner).Assembly
        .GetManifestResourceNames()
        .Count(static name =>
            name.StartsWith("AgentPrism.PostgreSql.Migrations.", StringComparison.Ordinal)
            && name.EndsWith(".sql", StringComparison.Ordinal));

    [Fact]
    public async Task Ilk_kosuda_sema_ve_tablolar_olusur()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture, applyMigrations: false);

        var applied = await context.Migrations.ApplyAsync();

        applied.ShouldBe(EmbeddedMigrationCount);

        var tableCount = await context.ScalarAsync<long>(
            $"SELECT count(*) FROM information_schema.tables WHERE table_schema = '{context.SchemaName}';");

        // 0001_initial 13 tablo + migration defteri, 0002_observability 2 tablo,
        // 0003_agent_skills 2 tablo, 0004_skill_scripts 2 tablo daha.
        // 0005_agent_call_graph YENI TABLO EKLEMEZ; runs tablosuna sutun ekler.
        // 0006_attachments 2 tablo daha (attachments, agent_files).
        // 0007_workflows 2 tablo daha (workflows, workflow_checkpoints) ve
        // runs tablosuna kind + workflow_name sutunlarini ekler.
        // 0008_scheduling 3 tablo daha (job_schedules, jobs, job_items).
        //
        // Sayi BILEREK sabittir: yeni bir tablo eklendiginde bu test kirilir ve
        // ekleyen kisi tabloyu fark etmis olur.
        tableCount.ShouldBe(27);
    }

    [Fact]
    public async Task Ikinci_kosu_hicbir_sey_uygulamaz()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture, applyMigrations: false);

        (await context.Migrations.ApplyAsync()).ShouldBe(EmbeddedMigrationCount);
        (await context.Migrations.ApplyAsync()).ShouldBe(0);
        (await context.Migrations.ApplyAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Uygulanan_migration_deftere_yazilir()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        var name = await context.ScalarAsync<string>(
            $"SELECT name FROM {context.SchemaName}.__migrations ORDER BY id LIMIT 1;");

        name.ShouldBe("0001_initial");

        var checksum = await context.ScalarAsync<string>(
            $"SELECT checksum FROM {context.SchemaName}.__migrations ORDER BY id LIMIT 1;");

        checksum.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Degistirilmis_migration_hata_verir()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        // Dosya degistirilmis gibi davranmak icin defterdeki ozeti bozuyoruz.
        await context.ExecuteAsync(
            $"UPDATE {context.SchemaName}.__migrations SET checksum = 'BOZUK' WHERE id = 1;");

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await context.Migrations.ApplyAsync());

        exception.Message.ShouldContain("0001_initial");
        exception.Message.ShouldContain("degismis");
    }

    [Fact]
    public async Task Bes_es_zamanli_kosuda_migration_tek_kez_uygulanir()
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

            // Tam olarak bir kosu migration'lari uygular; digerleri onun bitmesini
            // bekler ve uygulanmis bulur. pg_advisory_lock bunu garanti eder.
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

    [Fact]
    public async Task Gecersiz_sema_adi_reddedilir()
    {
        var options = Options.Create(new AgentPrismPostgreSqlOptions
        {
            ConnectionString = fixture.ConnectionString,
            SchemaName = "kotu-ad; DROP TABLE users",
        });

        await using var dataSource = new NpgsqlDataSourceBuilder(fixture.ConnectionString).Build();

        var exception = Should.Throw<AgentPrismException>(
            () => new MigrationRunner(dataSource, options, NullLogger<MigrationRunner>.Instance));

        exception.Message.ShouldContain("sema adi");
    }

    [Fact]
    public async Task Ozel_sema_adi_kullanilir()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = PostgresTestContext.Create(fixture, schemaName);

        await context.Migrations.ApplyAsync();

        var exists = await context.ScalarAsync<bool>(
            $"SELECT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = '{schemaName}');");

        exists.ShouldBeTrue();
    }
}
