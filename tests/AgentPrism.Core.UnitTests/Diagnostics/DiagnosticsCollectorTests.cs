using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Core.UnitTests.Diagnostics;

/// <summary>
/// Verifies that <see cref="AgentPrismDiagnosticsCollector"/> produces the correct
/// report for in-memory, SQL, and duplicate-registration (K-183) cases (Phase 33).
/// </summary>
public sealed class DiagnosticsCollectorTests
{
    [Fact]
    public async Task In_memory_setup_reports_InMemory_and_healthy()
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
    public async Task Unaudited_provider_reports_Unknown_and_circuit_closed()
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
    public async Task Single_registered_SQL_provider_reflects_name_and_migration_status()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism();
        services.AddSingleton(new SqlPersistenceRegistrationMarker("PostgreSQL"));
        services.AddSingleton<ISqlPersistenceDiagnostics>(
            new FakeSqlPersistenceDiagnostics("PostgreSQL", canConnect: true, pendingMigrations: ["003_add"]));

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<AgentPrismDiagnosticsCollector>();

        var report = await collector.CollectAsync();

        report.PersistenceProvider.ShouldBe("PostgreSQL");
        report.RegisteredPersistenceProviders.ShouldBe(1);
        report.CanConnect.ShouldBeTrue();
        report.MigrationsUpToDate.ShouldBeFalse();
        report.PendingMigrations.ShouldBe(["003_add"]);
    }

    [Fact]
    public async Task SQL_provider_that_cannot_connect_returns_CanConnect_false()
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
    public async Task Duplicate_SQL_registration_K183_counter_shows_two()
    {
        // Simulates two Use*() calls: the marker ACCUMULATES, the winner (ISqlPersistenceDiagnostics,
        // registered with Replace) is singular. In a real run MigrationHostedService logs a
        // startup warning for this (K-183); this test proves /api/diagnostics sees the same info.
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
    public async Task Same_configuration_key_from_multiple_providers_reports_single_row()
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
    public async Task Tool_and_agent_counts_are_reported()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism()
            .AddModelProvider(new FakeModelProvider())
            .AddTool((int a, int b) => a + b, "add", "adds two numbers");

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<AgentPrismDiagnosticsCollector>();

        var report = await collector.CollectAsync();

        report.ToolCount.ShouldBe(1);
        report.AgentCount.ShouldBe(0);
    }

    /// <summary>
    /// When the catalog cannot be read, the report does NOT crash; <c>AgentCount</c>
    /// comes back empty but migration info is preserved.
    /// </summary>
    /// <remarks>
    /// The primary use moment for the diagnostics endpoint is exactly when the schema
    /// is NOT yet ready (<c>AutoApplyMigrations=false</c>). Measured: MT-PG-051 — the
    /// endpoint returned HTTP 500 at the exact moment it was needed.
    /// </remarks>
    [Fact]
    public async Task Unreadable_catalog_still_produces_report_and_AgentCount_is_empty()
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
