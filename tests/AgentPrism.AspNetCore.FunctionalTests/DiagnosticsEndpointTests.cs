using System.Net;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies the <c>GET /api/diagnostics</c> endpoint (Phase 33, F-62): off by
/// default, requires the Admin role, leaks no <c>secret</c> value.
/// </summary>
public sealed class DiagnosticsEndpointTests
{
    private static readonly Uri Diagnostics = new("/agentprism/api/diagnostics", UriKind.Relative);

    [Fact]
    public async Task Endpoint_does_not_map_at_all_while_off_by_default()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(Diagnostics);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Returns_basic_fields_when_enabled()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true);

        using var response = await host.Client.GetAsync(Diagnostics);
        response.EnsureSuccessStatusCode();

        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        body.GetProperty("persistenceProvider").GetString().ShouldBe("InMemory");
        body.GetProperty("registeredPersistenceProviders").GetInt32().ShouldBe(0);
        body.GetProperty("canConnect").GetBoolean().ShouldBeTrue();
        body.GetProperty("migrationsUpToDate").GetBoolean().ShouldBeTrue();
        body.GetProperty("uiEmbedded").GetBoolean().ShouldBeFalse();

        // AgentPrismTestHost registers the "echo" provider by default.
        body.GetProperty("modelProviders").EnumerateArray()
            .ShouldContain(static p => string.Equals(p.GetProperty("name").GetString(), "echo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Gets_403_when_the_Admin_policy_fails()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true,
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        using var response = await host.Client.GetAsync(Diagnostics);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Gets_200_when_the_Admin_policy_succeeds()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true,
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Admin, static policy => policy.RequireAssertion(static _ => true)));

        using var response = await host.Client.GetAsync(Diagnostics);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Known_API_key_does_not_appear_anywhere_in_the_response()
    {
        const string secret = "super-secret-openai-key-TEST-4f9a";

        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.UseOpenAI(secret, o => o.Endpoint = server.BaseAddress),
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true);

        using var response = await host.Client.GetAsync(Diagnostics);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync();
        raw.ShouldNotContain(secret);

        var body = await AgentPrismTestHost.ReadJsonAsync(
            await host.Client.GetAsync(Diagnostics));

        var configuration = body.GetProperty("configuration").EnumerateArray().ToArray();
        var openAiKey = configuration.Single(
            static c => string.Equals(c.GetProperty("key").GetString(), "AgentPrism:Providers:OpenAI:ApiKey", StringComparison.Ordinal));
        openAiKey.GetProperty("resolved").GetBoolean().ShouldBeTrue();
        openAiKey.TryGetProperty("hint", out var hint).ShouldBeTrue();
        hint.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

}
