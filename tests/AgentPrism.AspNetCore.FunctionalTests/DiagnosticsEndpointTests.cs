using System.Net;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// <c>GET /api/diagnostics</c> ucunu dogrular (Faz 33, F-62): varsayilan kapali,
/// Admin rolu ister, hicbir <c>secret</c> deger sizdirmaz.
/// </summary>
public sealed class DiagnosticsEndpointTests
{
    private static readonly Uri Diagnostics = new("/agentprism/api/diagnostics", UriKind.Relative);

    [Fact]
    public async Task Varsayilan_kapaliyken_uc_hic_baglanmaz()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(Diagnostics);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Acikken_temel_alanlari_dondurur()
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

        // AgentPrismTestHost varsayilan olarak "echo" saglayicisini kaydeder.
        body.GetProperty("modelProviders").EnumerateArray()
            .ShouldContain(static p => string.Equals(p.GetProperty("name").GetString(), "echo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Admin_policy_basarisizsa_403_alir()
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
    public async Task Admin_policy_basariliysa_200_alir()
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
    public async Task Bilinen_API_anahtari_yanitin_hicbir_yerinde_gecmez()
    {
        const string secret = "cok-gizli-openai-anahtari-DENEME-4f9a";

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
