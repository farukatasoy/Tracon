using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;
using AgentPrism.StoreContracts;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// Tuketicinin <c>public</c> semasina dokunulmadigini dogrular.
/// </summary>
/// <remarks>Bu bir guvenlik ve guven sinirdir; karar K-013.</remarks>
public sealed class SchemaIsolationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Public_semasi_degismez()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture, applyMigrations: false);

        // Tuketicinin var olan tablosunu taklit ediyoruz.
        await context.ExecuteAsync("CREATE TABLE IF NOT EXISTS public.musteri_siparisleri (id integer PRIMARY KEY);");

        var before = await ReadPublicTablesAsync(context);

        await context.Migrations.ApplyAsync();

        var after = await ReadPublicTablesAsync(context);

        after.ShouldBe(before);
        after.ShouldContain(static table => string.Equals(table, "musteri_siparisleri", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tablolar_yalnizca_agentprism_semasinda_olusur()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        var agentPrismTables = await context.ScalarAsync<long>(
            $"SELECT count(*) FROM information_schema.tables WHERE table_schema = '{context.SchemaName}';");

        agentPrismTables.ShouldBeGreaterThan(0);

        var leaked = await context.ScalarAsync<long>(
            """
            SELECT count(*) FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name IN
                ('runs', 'run_events', 'sessions', 'agent_definitions', 'conversations', '__migrations');
            """);

        leaked.ShouldBe(0);
    }

    private static async ValueTask<List<string>> ReadPublicTablesAsync(PostgresTestContext context)
    {
        var tables = new List<string>();

        await using var command = context.DataSource.CreateCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name;");

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}

/// <summary>Bir kiracinin digerinin verisini goremedigini dogrular.</summary>
public sealed class TenantIsolationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Kiraci_digerinin_agent_tanimini_goremez()
    {
        var schemaName = PostgresTestContext.NewSchemaName();

        await using var alpha = PostgresTestContext.Create(fixture, schemaName, tenantId: "alpha");
        await using var beta = PostgresTestContext.Create(fixture, schemaName, tenantId: "beta");

        await alpha.Migrations.ApplyAsync();

        await alpha.AgentDefinitions.SaveAsync(TestData.Definition("gizli"));

        (await alpha.AgentDefinitions.GetAsync("gizli")).ShouldNotBeNull();
        (await beta.AgentDefinitions.GetAsync("gizli")).ShouldBeNull();
        (await beta.AgentDefinitions.ListAsync()).ShouldBeEmpty();
        (await beta.AgentDefinitions.ListVersionsAsync("gizli")).ShouldBeEmpty();
        (await beta.AgentDefinitions.DeleteAsync("gizli")).ShouldBeFalse();

        // Silme denemesinden sonra kayit hala yerinde.
        (await alpha.AgentDefinitions.GetAsync("gizli")).ShouldNotBeNull();
    }

    [Fact]
    public async Task Ayni_agent_adi_iki_kiracida_bagimsiz_yasar()
    {
        var schemaName = PostgresTestContext.NewSchemaName();

        await using var alpha = PostgresTestContext.Create(fixture, schemaName, tenantId: "alpha");
        await using var beta = PostgresTestContext.Create(fixture, schemaName, tenantId: "beta");

        await alpha.Migrations.ApplyAsync();

        await alpha.AgentDefinitions.SaveAsync(TestData.Definition("destek") with { Instructions = "alpha talimati" });
        await beta.AgentDefinitions.SaveAsync(TestData.Definition("destek") with { Instructions = "beta talimati" });

        (await alpha.AgentDefinitions.GetAsync("destek"))!.Instructions.ShouldBe("alpha talimati");
        (await beta.AgentDefinitions.GetAsync("destek"))!.Instructions.ShouldBe("beta talimati");

        // Her kiracinin surum sayaci kendine aittir.
        (await alpha.AgentDefinitions.GetAsync("destek"))!.Version.ShouldBe(1);
        (await beta.AgentDefinitions.GetAsync("destek"))!.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Kiraci_digerinin_calistirmasini_goremez()
    {
        var schemaName = PostgresTestContext.NewSchemaName();

        await using var alpha = PostgresTestContext.Create(fixture, schemaName, tenantId: "alpha");
        await using var beta = PostgresTestContext.Create(fixture, schemaName, tenantId: "beta");

        await alpha.Migrations.ApplyAsync();

        var runId = AgentPrismId.NewId();
        await alpha.Runs.StartRunAsync(TestData.Run(runId));
        await alpha.Runs.AppendEventAsync(TestData.Event(runId, 0));

        (await alpha.Runs.GetRunAsync(runId)).ShouldNotBeNull();
        (await beta.Runs.GetRunAsync(runId)).ShouldBeNull();
        (await beta.Runs.QueryRunsAsync(new RunQuery())).ShouldBeEmpty();

        var betaEvents = new List<RunEvent>();

        await foreach (var runEvent in beta.Runs.ReadEventsAsync(runId))
        {
            betaEvents.Add(runEvent);
        }

        betaEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Kiraci_digerinin_oturumunu_goremez()
    {
        var schemaName = PostgresTestContext.NewSchemaName();

        await using var alpha = PostgresTestContext.Create(fixture, schemaName, tenantId: "alpha");
        await using var beta = PostgresTestContext.Create(fixture, schemaName, tenantId: "beta");

        await alpha.Migrations.ApplyAsync();

        await alpha.Sessions.SaveAsync(TestData.Session("ortak-kimlik"));

        (await alpha.Sessions.GetAsync("ortak-kimlik")).ShouldNotBeNull();
        (await beta.Sessions.GetAsync("ortak-kimlik")).ShouldBeNull();
        (await beta.Sessions.QueryAsync(new SessionQuery())).ShouldBeEmpty();
        (await beta.Sessions.DeleteAsync("ortak-kimlik")).ShouldBeFalse();
    }

    [Fact]
    public async Task Calistirma_kaydi_gecerli_kiraciyla_damgalanir()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = PostgresTestContext.Create(fixture, schemaName, tenantId: "alpha");

        await context.Migrations.ApplyAsync();

        var runId = AgentPrismId.NewId();
        var record = await context.Runs.StartRunAsync(TestData.Run(runId));

        record.TenantId.ShouldBe("alpha");
        (await context.Runs.GetRunAsync(runId))!.TenantId.ShouldBe("alpha");
    }
}
