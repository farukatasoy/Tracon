using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Core.UnitTests.Diagnostics;

/// <summary>
/// <see cref="AgentPrismDiagnosticsCollector"/>'in bellek ici, SQL ve cift kayit
/// (K-183) durumlarinda dogru rapor urettigini dogrular (Faz 33).
/// </summary>
public sealed class DiagnosticsCollectorTests
{
    [Fact]
    public async Task Bellek_ici_kurulum_InMemory_ve_saglikli_raporlanir()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().AddModelProvider(new FakeModelProvider());

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<AgentPrismDiagnosticsCollector>();

        var report = await collector.CollectAsync();

        report.PersistenceProvider.ShouldBe("InMemory");
        report.RegisteredPersistenceProviders.ShouldBe(0);
        report.CanConnect.ShouldBeTrue();
        report.MigrationsUpToDate.ShouldBeTrue();
        report.PendingMigrations.ShouldBeEmpty();
        report.UiEmbedded.ShouldBeFalse();
        report.ModelProviders.ShouldHaveSingleItem().Name.ShouldBe("fake");
    }

    [Fact]
    public async Task Denetlenmemis_saglayici_Unknown_ve_devre_kapali_raporlanir()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().AddModelProvider(new FakeModelProvider());

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<AgentPrismDiagnosticsCollector>();

        var report = await collector.CollectAsync();

        var diagnostic = report.ModelProviders.ShouldHaveSingleItem();
        diagnostic.Status.ShouldBe("Unknown");
        diagnostic.CircuitOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task Tek_SQL_saglayicisi_kayitliysa_adi_ve_migration_durumu_yansir()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism();
        services.AddSingleton(new SqlPersistenceRegistrationMarker("PostgreSQL"));
        services.AddSingleton<ISqlPersistenceDiagnostics>(
            new FakeSqlPersistenceDiagnostics("PostgreSQL", canConnect: true, pendingMigrations: ["003_ekle"]));

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<AgentPrismDiagnosticsCollector>();

        var report = await collector.CollectAsync();

        report.PersistenceProvider.ShouldBe("PostgreSQL");
        report.RegisteredPersistenceProviders.ShouldBe(1);
        report.CanConnect.ShouldBeTrue();
        report.MigrationsUpToDate.ShouldBeFalse();
        report.PendingMigrations.ShouldBe(["003_ekle"]);
    }

    [Fact]
    public async Task Baglanti_kurulamayan_SQL_saglayicisi_CanConnect_false_doner()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism();
        services.AddSingleton(new SqlPersistenceRegistrationMarker("SQL Server"));
        services.AddSingleton<ISqlPersistenceDiagnostics>(
            new FakeSqlPersistenceDiagnostics("SQL Server", canConnect: false, pendingMigrations: []));

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<AgentPrismDiagnosticsCollector>();

        var report = await collector.CollectAsync();

        report.CanConnect.ShouldBeFalse();
        report.MigrationsUpToDate.ShouldBeFalse();
    }

    [Fact]
    public async Task Cift_SQL_kaydi_K183_sayaci_ikiyi_gosterir()
    {
        // İki Use*() cagrisini taklit eder: isaret BIRIKIR, kazanan (ISqlPersistenceDiagnostics,
        // Replace ile kaydedilir) tekildir. Gercek kosuda MigrationHostedService bunun icin
        // acilista uyari loglar (K-183); bu test /api/diagnostics'in ayni bilgiyi gordugunu kanitlar.
        var services = new ServiceCollection();
        services.AddAgentPrism();
        services.AddSingleton(new SqlPersistenceRegistrationMarker("PostgreSQL"));
        services.AddSingleton(new SqlPersistenceRegistrationMarker("SQLite"));
        services.AddSingleton<ISqlPersistenceDiagnostics>(
            new FakeSqlPersistenceDiagnostics("SQLite", canConnect: true, pendingMigrations: []));

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<AgentPrismDiagnosticsCollector>();

        var report = await collector.CollectAsync();

        report.RegisteredPersistenceProviders.ShouldBe(2);
        report.PersistenceProvider.ShouldBe("SQLite");
    }

    [Fact]
    public async Task Ayni_yapilandirma_anahtari_birden_fazla_saglayicidan_gelirse_tek_satir_raporlanir()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism()
            .AddModelProvider(new FakeConfigurationDiagnosticProvider("a", "AgentPrism:Providers:X:ApiKey", resolved: true))
            .AddModelProvider(new FakeConfigurationDiagnosticProvider("b", "AgentPrism:Providers:X:ApiKey", resolved: true));

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<AgentPrismDiagnosticsCollector>();

        var report = await collector.CollectAsync();

        report.Configuration.ShouldHaveSingleItem().Key.ShouldBe("AgentPrism:Providers:X:ApiKey");
    }

    [Fact]
    public async Task Tool_ve_agent_sayilari_raporlanir()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism()
            .AddModelProvider(new FakeModelProvider())
            .AddTool((int a, int b) => a + b, "topla", "iki sayiyi toplar");

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<AgentPrismDiagnosticsCollector>();

        var report = await collector.CollectAsync();

        report.ToolCount.ShouldBe(1);
        report.AgentCount.ShouldBe(0);
    }

    /// <summary>
    /// Katalog okunamadiginda rapor COKMEZ; <c>AgentCount</c> bos gelir ama
    /// migration bilgisi korunur.
    /// </summary>
    /// <remarks>
    /// Teshis ucunun birincil kullanim ani semanin HENUZ hazir olmadigi andir
    /// (<c>AutoApplyMigrations=false</c>). Olculdu: MT-PG-051 — uc tam da
    /// ihtiyac duyuldugu anda HTTP 500 donuyordu.
    /// </remarks>
    [Fact]
    public async Task Katalog_okunamazsa_rapor_uretilir_ve_AgentCount_bos_gelir()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().AddModelProvider(new FakeModelProvider());
        services.AddSingleton<IAgentCatalog>(new ThrowingAgentCatalog());

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<AgentPrismDiagnosticsCollector>();

        var report = await collector.CollectAsync();

        report.AgentCount.ShouldBeNull();
        report.PersistenceProvider.ShouldNotBeNullOrEmpty();
    }

    private sealed class ThrowingAgentCatalog : IAgentCatalog
    {
        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("relation \"agent_definitions\" does not exist");

        public ValueTask<Microsoft.Agents.AI.AIAgent?> ResolveAsync(
            string agentName,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("relation \"agent_definitions\" does not exist");

        public ValueTask<Microsoft.Agents.AI.AIAgent?> ResolveAsync(
            string agentName,
            int? version,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("relation \"agent_definitions\" does not exist");
    }
}
