using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies the <c>GET /api/diagnostics</c> endpoint (Phase 33, F-62): off by
/// default, requires the Admin role, leaks no <c>secret</c> value.
/// </summary>
public sealed class DiagnosticsEndpointTests
{
    private static readonly Uri Diagnostics = new("/tracon/api/diagnostics", UriKind.Relative);

    [Fact]
    public async Task Endpoint_does_not_map_at_all_while_off_by_default()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(Diagnostics);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Returns_basic_fields_when_enabled()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true);

        using var response = await host.Client.GetAsync(Diagnostics);
        response.EnsureSuccessStatusCode();

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("persistenceProvider").GetString().ShouldBe("InMemory");
        body.GetProperty("registeredPersistenceProviders").GetInt32().ShouldBe(0);
        body.GetProperty("canConnect").GetBoolean().ShouldBeTrue();
        body.GetProperty("migrationsUpToDate").GetBoolean().ShouldBeTrue();
        body.GetProperty("uiEmbedded").GetBoolean().ShouldBeFalse();

        // TraconTestHost registers the "echo" provider by default.
        body.GetProperty("modelProviders").EnumerateArray()
            .ShouldContain(static p => string.Equals(p.GetProperty("name").GetString(), "echo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_report_names_the_catalog_models_that_carry_no_price()
    {
        // 🚨 A run answered by an unpriced model is recorded with empty cost
        // columns and PricingSource.Unknown — honest, never zero. The mechanism
        // was right and said so nowhere, so an installation could record
        // thousands of costless runs before anyone read the pricing_source
        // value on a row to find out why.
        await using var host = await TraconTestHost.StartAsync(
            builder => builder.AddModelProvider(new Tracon.Testing.FakeModelProvider("priced")
                    .WithModel(new ModelDescriptor { Name = "cheap-1", InputCostPerMillionTokens = 0.25m }))
                .AddModelProvider(new Tracon.Testing.FakeModelProvider("unpriced")
                    .WithModel(new ModelDescriptor { Name = "mystery-1" })),
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true);

        using var response = await host.Client.GetAsync(Diagnostics);
        response.EnsureSuccessStatusCode();

        var pricing = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("pricing");

        var unpriced = pricing.GetProperty("unpricedModels").EnumerateArray()
            .Select(static model => model.GetString())
            .ToList();

        unpriced.ShouldContain(static model => string.Equals(model, "unpriced/mystery-1", StringComparison.Ordinal));
        unpriced.ShouldNotContain(static model => string.Equals(model, "priced/cheap-1", StringComparison.Ordinal));

        // The default "echo" provider TraconTestHost registers carries no
        // catalog at all, so it adds nothing to either count.
        pricing.GetProperty("pricedModels").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Green_field_setup_reports_all_seven_embedding_points_as_built_in_default()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true);

        var body = await TraconTestHost.ReadJsonAsync(await host.Client.GetAsync(Diagnostics));

        var extensionPoints = body.GetProperty("extensionPoints").EnumerateArray().ToArray();

        extensionPoints.Length.ShouldBe(7);
        extensionPoints.ShouldAllBe(static point => point.GetProperty("isBuiltInDefault").GetBoolean());
        extensionPoints.Select(static point => point.GetProperty("contract").GetString()).ShouldBe(
            ["ITenantContext", "IRunAttributionContext", "IToolAuthorizationHandler", "IRunAuthorizationHandler", "IRunEventSink", "IAttachmentStorage", "IToolApprovalPresenter"]);
    }

    [Fact]
    public async Task Host_bound_registration_reports_isBuiltInDefault_false_with_its_own_type_name()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services.AddSingleton<ITenantContext, HostTenantContext>(),
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true);

        var body = await TraconTestHost.ReadJsonAsync(await host.Client.GetAsync(Diagnostics));

        var tenantPoint = body.GetProperty("extensionPoints").EnumerateArray()
            .Single(static point => string.Equals(point.GetProperty("contract").GetString(), "ITenantContext", StringComparison.Ordinal));

        tenantPoint.GetProperty("isBuiltInDefault").GetBoolean().ShouldBeFalse();
        tenantPoint.GetProperty("implementation").GetString().ShouldBe(nameof(HostTenantContext));
    }

    private sealed class HostTenantContext : ITenantContext
    {
        public string TenantId => "host-tenant";
    }

    /// <summary>101.3: registered agent sources — built-in and custom — appear in priority order.</summary>
    [Fact]
    public async Task Registered_agent_sources_are_reported_in_priority_order()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder.AddAgentSource(new StubAgentSource("git", 200)),
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true);

        var body = await TraconTestHost.ReadJsonAsync(await host.Client.GetAsync(Diagnostics));

        var sources = body.GetProperty("agentSources").EnumerateArray().ToArray();

        sources.Select(static s => s.GetProperty("name").GetString()).ShouldBe(["code", "database", "git"]);
        sources.Select(static s => s.GetProperty("priority").GetInt32()).ShouldBe([0, 100, 200]);
        sources.Single(static s => string.Equals(s.GetProperty("name").GetString(), "git", StringComparison.Ordinal))
            .GetProperty("implementation").GetString()!.ShouldContain(nameof(StubAgentSource));
    }

    private sealed class StubAgentSource(string name, int priority) : IAgentSource
    {
        public string Name { get; } = name;

        public int Priority { get; } = priority;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)[]);

        public ValueTask<Microsoft.Agents.AI.AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
            => new((Microsoft.Agents.AI.AIAgent?)null);
    }

    [Fact]
    public async Task Gets_403_when_the_Admin_policy_fails()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true,
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(TraconPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        using var response = await host.Client.GetAsync(Diagnostics);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Gets_200_when_the_Admin_policy_succeeds()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true,
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(TraconPolicies.Admin, static policy => policy.RequireAssertion(static _ => true)));

        using var response = await host.Client.GetAsync(Diagnostics);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Known_API_key_does_not_appear_anywhere_in_the_response()
    {
        const string secret = "super-secret-openai-key-TEST-4f9a";

        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder.UseOpenAI(secret, o => o.Endpoint = server.BaseAddress),
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true);

        using var response = await host.Client.GetAsync(Diagnostics);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync();
        raw.ShouldNotContain(secret);

        var body = await TraconTestHost.ReadJsonAsync(
            await host.Client.GetAsync(Diagnostics));

        var configuration = body.GetProperty("configuration").EnumerateArray().ToArray();
        var openAiKey = configuration.Single(
            static c => string.Equals(c.GetProperty("key").GetString(), "Tracon:Providers:OpenAI:ApiKey", StringComparison.Ordinal));
        openAiKey.GetProperty("resolved").GetBoolean().ShouldBeTrue();
        openAiKey.TryGetProperty("hint", out var hint).ShouldBeTrue();
        hint.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

}
