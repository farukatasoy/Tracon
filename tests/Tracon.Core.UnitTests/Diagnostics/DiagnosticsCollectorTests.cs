using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// Verifies that <see cref="TraconDiagnosticsCollector"/> produces the correct
/// report for in-memory, SQL, and duplicate-registration cases.
/// </summary>
public sealed class DiagnosticsCollectorTests
{
    [Fact]
    public async Task In_memory_setup_reports_InMemory_and_healthy()
    {
        var services = new ServiceCollection();
        services.AddTracon().AddModelProvider(new FakeModelProvider());

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<TraconDiagnosticsCollector>();

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
        services.AddTracon().AddModelProvider(new FakeModelProvider());

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<TraconDiagnosticsCollector>();

        var report = await collector.CollectAsync();

        var diagnostic = report.ModelProviders.ShouldHaveSingleItem();
        diagnostic.Status.ShouldBe("Unknown");
        diagnostic.CircuitOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task Single_registered_SQL_provider_reflects_name_and_migration_status()
    {
        var services = new ServiceCollection();
        services.AddTracon();
        services.AddSingleton(new SqlPersistenceRegistrationMarker("PostgreSQL"));
        services.AddSingleton<ISqlPersistenceDiagnostics>(
            new FakeSqlPersistenceDiagnostics("PostgreSQL", canConnect: true, pendingMigrations: ["003_add"]));

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<TraconDiagnosticsCollector>();

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
        services.AddTracon();
        services.AddSingleton(new SqlPersistenceRegistrationMarker("SQL Server"));
        services.AddSingleton<ISqlPersistenceDiagnostics>(
            new FakeSqlPersistenceDiagnostics("SQL Server", canConnect: false, pendingMigrations: []));

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<TraconDiagnosticsCollector>();

        var report = await collector.CollectAsync();

        report.CanConnect.ShouldBeFalse();
        report.MigrationsUpToDate.ShouldBeFalse();
    }

    [Fact]
    public async Task Duplicate_SQL_registration_K183_counter_shows_two()
    {
        // Simulates two Use*() calls: the marker ACCUMULATES, the winner (ISqlPersistenceDiagnostics,
        // registered with Replace) is singular. In a real run MigrationHostedService logs a
        // startup warning for this; this test proves /api/diagnostics sees the same info.
        var services = new ServiceCollection();
        services.AddTracon();
        services.AddSingleton(new SqlPersistenceRegistrationMarker("PostgreSQL"));
        services.AddSingleton(new SqlPersistenceRegistrationMarker("SQLite"));
        services.AddSingleton<ISqlPersistenceDiagnostics>(
            new FakeSqlPersistenceDiagnostics("SQLite", canConnect: true, pendingMigrations: []));

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<TraconDiagnosticsCollector>();

        var report = await collector.CollectAsync();

        report.RegisteredPersistenceProviders.ShouldBe(2);
        report.PersistenceProvider.ShouldBe("SQLite");
    }

    [Fact]
    public async Task Same_configuration_key_from_multiple_providers_reports_single_row()
    {
        var services = new ServiceCollection();
        services.AddTracon()
            .AddModelProvider(new FakeConfigurationDiagnosticProvider("a", "Tracon:Providers:X:ApiKey", resolved: true))
            .AddModelProvider(new FakeConfigurationDiagnosticProvider("b", "Tracon:Providers:X:ApiKey", resolved: true));

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<TraconDiagnosticsCollector>();

        var report = await collector.CollectAsync();

        report.Configuration.ShouldHaveSingleItem().Key.ShouldBe("Tracon:Providers:X:ApiKey");
    }

    [Fact]
    public async Task Tool_and_agent_counts_are_reported()
    {
        var services = new ServiceCollection();
        services.AddTracon()
            .AddModelProvider(new FakeModelProvider())
            .AddTool((int a, int b) => a + b, "add", "adds two numbers");

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<TraconDiagnosticsCollector>();

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
    /// is NOT yet ready (<c>AutoApplyMigrations=false</c>). Measured: an earlier defect — the
    /// endpoint returned HTTP 500 at the exact moment it was needed.
    /// </remarks>
    [Fact]
    public async Task Unreadable_catalog_still_produces_report_and_AgentCount_is_empty()
    {
        var services = new ServiceCollection();
        services.AddTracon().AddModelProvider(new FakeModelProvider());
        services.AddSingleton<IAgentCatalog>(new ThrowingAgentCatalog());

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<TraconDiagnosticsCollector>();

        var report = await collector.CollectAsync();

        report.AgentCount.ShouldBeNull();
        report.PersistenceProvider.ShouldNotBeNullOrEmpty();
    }

    // --- Embedding points ---

    [Fact]
    public async Task Bare_setup_reports_all_seven_embedding_points_as_built_in_default()
    {
        var services = new ServiceCollection();
        services.AddTracon().AddModelProvider(new FakeModelProvider());

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<TraconDiagnosticsCollector>();

        var report = await collector.CollectAsync();

        report.ExtensionPoints.Count.ShouldBe(7);
        report.ExtensionPoints.ShouldAllBe(static point => point.IsBuiltInDefault);
        report.ExtensionPoints.Select(static point => point.Contract).ShouldBe(
            [nameof(ITenantContext), nameof(IRunAttributionContext), nameof(IToolAuthorizationHandler), nameof(IRunAuthorizationHandler), nameof(IRunEventSink), nameof(IAttachmentStorage), nameof(IToolApprovalPresenter)]);

        var sink = report.ExtensionPoints.Single(
            static point => string.Equals(point.Contract, nameof(IRunEventSink), StringComparison.Ordinal));
        sink.Implementation.ShouldBe("(none)");

        var storage = report.ExtensionPoints.Single(
            static point => string.Equals(point.Contract, nameof(IAttachmentStorage), StringComparison.Ordinal));
        storage.Implementation.ShouldBe("(database)");

        var presenter = report.ExtensionPoints.Single(
            static point => string.Equals(point.Contract, nameof(IToolApprovalPresenter), StringComparison.Ordinal));
        presenter.Implementation.ShouldBe(nameof(NullToolApprovalPresenter));
    }

    [Fact]
    public async Task Host_bound_implementations_report_their_own_type_and_not_built_in()
    {
        var services = new ServiceCollection();

        // TryAdd semantics: a registration made BEFORE AddTracon() wins over
        // the built-in default for the three singleton contracts.
        services.AddSingleton<ITenantContext, FixedTenantContext>();
        services.AddSingleton<IRunAttributionContext, FixedRunAttributionContext>();
        services.AddSingleton<IToolAuthorizationHandler, DenyAllToolAuthorizationHandler>();
        services.AddSingleton<IRunAuthorizationHandler, DenyAllRunAuthorizationHandler>();
        services.AddSingleton<IRunEventSink, RecordingRunEventSink>();
        services.AddSingleton<IAttachmentStorage, FakeAttachmentStorage>();
        services.AddSingleton<IToolApprovalPresenter, FakeToolApprovalPresenter>();

        services.AddTracon().AddModelProvider(new FakeModelProvider());

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<TraconDiagnosticsCollector>();

        var report = await collector.CollectAsync();

        report.ExtensionPoints.Count.ShouldBe(7);
        report.ExtensionPoints.ShouldAllBe(static point => !point.IsBuiltInDefault);

        report.ExtensionPoints.Single(
                static point => string.Equals(point.Contract, nameof(ITenantContext), StringComparison.Ordinal))
            .Implementation.ShouldBe(nameof(FixedTenantContext));
        report.ExtensionPoints.Single(
                static point => string.Equals(point.Contract, nameof(IRunAttributionContext), StringComparison.Ordinal))
            .Implementation.ShouldBe(nameof(FixedRunAttributionContext));
        report.ExtensionPoints.Single(
                static point => string.Equals(point.Contract, nameof(IToolAuthorizationHandler), StringComparison.Ordinal))
            .Implementation.ShouldBe(nameof(DenyAllToolAuthorizationHandler));
        report.ExtensionPoints.Single(
                static point => string.Equals(point.Contract, nameof(IRunAuthorizationHandler), StringComparison.Ordinal))
            .Implementation.ShouldBe(nameof(DenyAllRunAuthorizationHandler));
        report.ExtensionPoints.Single(
                static point => string.Equals(point.Contract, nameof(IRunEventSink), StringComparison.Ordinal))
            .Implementation.ShouldBe(nameof(RecordingRunEventSink));
        report.ExtensionPoints.Single(
                static point => string.Equals(point.Contract, nameof(IAttachmentStorage), StringComparison.Ordinal))
            .Implementation.ShouldBe(nameof(FakeAttachmentStorage));
        report.ExtensionPoints.Single(
                static point => string.Equals(point.Contract, nameof(IToolApprovalPresenter), StringComparison.Ordinal))
            .Implementation.ShouldBe(nameof(FakeToolApprovalPresenter));
    }

    [Fact]
    public async Task Multiple_registered_sinks_are_all_named_and_not_built_in()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRunEventSink, RecordingRunEventSink>();
        services.AddSingleton<IRunEventSink, AnotherRunEventSink>();
        services.AddTracon().AddModelProvider(new FakeModelProvider());

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<TraconDiagnosticsCollector>();

        var report = await collector.CollectAsync();

        var sink = report.ExtensionPoints.Single(
            static point => string.Equals(point.Contract, nameof(IRunEventSink), StringComparison.Ordinal));
        sink.IsBuiltInDefault.ShouldBeFalse();
        sink.Implementation.ShouldBe($"{nameof(RecordingRunEventSink)}, {nameof(AnotherRunEventSink)}");
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "fixed-tenant";
    }

    private sealed class FixedRunAttributionContext : IRunAttributionContext
    {
        public string? UserId => "fixed-user";

        public IReadOnlyDictionary<string, string>? Labels => null;
    }

    private sealed class DenyAllToolAuthorizationHandler : IToolAuthorizationHandler
    {
        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(
            ToolAuthorizationRequest request,
            CancellationToken cancellationToken = default)
            => new(ToolAuthorizationResult.Deny("denied by test"));
    }

    private sealed class DenyAllRunAuthorizationHandler : IRunAuthorizationHandler
    {
        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
            RunAuthorizationRequest request,
            CancellationToken cancellationToken = default)
            => new(RunAuthorizationResult.Deny("denied by test"));

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(
            SessionAuthorizationRequest request,
            CancellationToken cancellationToken = default)
            => new(RunAuthorizationResult.Deny("denied by test"));
    }

    private sealed class FakeToolApprovalPresenter : IToolApprovalPresenter
    {
        public ValueTask<ToolApprovalPresentation?> PresentAsync(
            ToolApprovalContext context, CancellationToken cancellationToken = default)
            => new((ToolApprovalPresentation?)null);
    }

    private sealed class RecordingRunEventSink : IRunEventSink
    {
        public ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default) => default;
    }

    [Fact]
    public async Task A_model_only_a_saved_definition_names_is_not_in_the_report_but_warns_once_when_a_run_prices_it()
    {
        // 🚨 The report names the models the HOST configured. A saved definition belongs to a tenant: reading every tenant's
        // definitions to fill a report has an unknown cost and crosses the tenant boundary. The line a run writes when it finds
        // no price is what covers them, and it names the model and the provider only.
        var logs = new WarningLogProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(logs);
        });
        services.AddTracon().AddModelProvider(new FakeModelProvider());

        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IAgentDefinitionStore>().SaveAsync(
            new AgentDefinition
            {
                Name = "ghost-agent",
                Model = new ModelBinding { Provider = "fake", Model = "ghost-model-1" },
            },
            TestContext.Current.CancellationToken);

        var resolver = provider.GetRequiredService<IRunPricingResolver>();
        var usage = new RunUsage { InputTokens = 10, OutputTokens = 5 };

        resolver.Resolve("fake", "ghost-model-1", usage).ShouldNotBeNull().Source.ShouldBe(PricingSource.Unknown);
        resolver.Resolve("fake", "ghost-model-1", usage).ShouldNotBeNull().Source.ShouldBe(PricingSource.Unknown);

        var report = await provider.GetRequiredService<TraconDiagnosticsCollector>().CollectAsync(TestContext.Current.CancellationToken);

        report.Pricing.UnpricedModels.ShouldNotContain(static model => model.Contains("ghost", StringComparison.Ordinal));
        report.Pricing.UnpricedMediaModels.ShouldNotContain(static model => model.Contains("ghost", StringComparison.Ordinal));

        var warning = logs.Warnings.Where(static line => line.Contains("ghost-model-1", StringComparison.Ordinal)).ShouldHaveSingleItem();
        warning.ShouldContain("fake");
    }

    [Fact]
    public async Task The_report_names_no_media_model_when_the_host_configured_none()
    {
        var services = new ServiceCollection();
        services.AddTracon();

        await using var provider = services.BuildServiceProvider();

        var report = await provider.GetRequiredService<TraconDiagnosticsCollector>().CollectAsync(TestContext.Current.CancellationToken);

        report.Pricing.UnpricedMediaModels.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_image_model_the_host_configured_is_named_until_it_has_a_price()
    {
        var unpriced = await ReportedImageModelsAsync(static _ => { });
        var priced = await ReportedImageModelsAsync(static options =>
            options.Pricing.Images["studio"] = new Dictionary<string, ImagePriceOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["studio-img-1"] = new() { PerImage = 0.04m },
            });

        unpriced.ShouldBe(["studio/studio-img-1 (image generation)"]);
        priced.ShouldBeEmpty();
    }

    private static async Task<IReadOnlyList<string>> ReportedImageModelsAsync(Action<TraconOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddTracon();
        services.Configure(configure);

        // The tool registry resolves the generator while image generation is on, so the host must have registered one.
        StudioImageGenerator.Register(services, "studio");
        services.Configure<TraconImageOptions>(static options =>
        {
            options.Enabled = true;
            options.Provider = "studio";
            options.Providers["studio"] = new TraconImageProviderOptions { Model = "studio-img-1" };
        });

        await using var provider = services.BuildServiceProvider();

        var report = await provider.GetRequiredService<TraconDiagnosticsCollector>().CollectAsync(TestContext.Current.CancellationToken);

        return report.Pricing.UnpricedMediaModels;
    }

#pragma warning disable MEAI001 // The image generator contract is experimental.
    private sealed class StudioImageGenerator : Microsoft.Extensions.AI.IImageGenerator
    {
        public static void Register(IServiceCollection services, string name)
            => services.AddKeyedSingleton<Microsoft.Extensions.AI.IImageGenerator>(name, new StudioImageGenerator());

        public Task<Microsoft.Extensions.AI.ImageGenerationResponse> GenerateAsync(
            Microsoft.Extensions.AI.ImageGenerationRequest request,
            Microsoft.Extensions.AI.ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new Microsoft.Extensions.AI.ImageGenerationResponse());

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
            // Nothing to release.
        }
    }
#pragma warning restore MEAI001

    private sealed class AnotherRunEventSink : IRunEventSink
    {
        public ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default) => default;
    }

    private sealed class FakeAttachmentStorage : IAttachmentStorage
    {
        public ValueTask<Uri> WriteAsync(
            string tenantId,
            Guid id,
            Stream content,
            string mediaType,
            CancellationToken cancellationToken = default)
            => new(new Uri($"fake://{tenantId}/{id}"));

        public ValueTask<Stream?> ReadAsync(Uri uri, CancellationToken cancellationToken = default) => new((Stream?)null);

        public ValueTask DeleteAsync(Uri uri, CancellationToken cancellationToken = default) => default;
    }

    private sealed class ThrowingAgentCatalog : IAgentCatalog
    {
        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("relation \"agent_definitions\" does not exist");

        public ValueTask<Microsoft.Agents.AI.AIAgent?> ResolveAsync(string agentName, AgentResolveOptions? options = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("relation \"agent_definitions\" does not exist");
    }
}
