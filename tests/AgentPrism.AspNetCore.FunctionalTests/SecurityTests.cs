using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Uc katmanli erisim korumasinin her katmanini ayri ayri dogrular.
/// </summary>
public sealed class SecurityTests
{
    private const string Token = "cok-gizli-token-DENEME-4c1f8a";

    // --- 1. katman: loopback kisiti ---

    [Fact]
    public async Task Uzak_ip_varsayilan_olarak_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Add(AgentPrismTestHost.RemoteIpHeader, "203.0.113.7");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Loopback_ip_gecer()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Add(AgentPrismTestHost.RemoteIpHeader, "127.0.0.1");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task IPv6_loopback_gecer()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Add(AgentPrismTestHost.RemoteIpHeader, "::1");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task IPv6ya_eslenmis_IPv4_loopback_gecer()
    {
        // ::ffff:127.0.0.1 — IPAddress.IsLoopback bu bicimi tek basina tanimaz.
        await using var host = await AgentPrismTestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Add(AgentPrismTestHost.RemoteIpHeader, "::ffff:127.0.0.1");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Uzak_erisim_acikken_uzak_ip_gecer()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AllowRemoteAccess = true);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Add(AgentPrismTestHost.RemoteIpHeader, "203.0.113.7");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --- 2. katman: bearer token ---

    [Fact]
    public async Task Token_isteniyorken_bassiz_istek_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/agents", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().ShouldContain("Bearer", Case.Sensitive);
    }

    [Fact]
    public async Task Yanlis_token_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "yanlis-token");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ayni_uzunlukta_yanlis_token_reddedilir()
    {
        // Sabit zamanli karsilastirmanin uzunluk esitliginde de dogru sonuc
        // verdigini gosterir: son karakter farkli.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            string.Concat(Token.AsSpan(0, Token.Length - 1), "b"));

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dogru_token_gecer()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Bearer_disinda_bir_sema_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Token);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // --- 3. katman: authorization policy ---

    [Fact]
    public async Task Basarisiz_policy_erisimi_engeller()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.RequireAuthorization("AgentPrismAdmin"),
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy("AgentPrismAdmin", static policy => policy.RequireAssertion(static _ => false)));

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/agents", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Basarili_policy_erisime_izin_verir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.RequireAuthorization("AgentPrismAdmin"),
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy("AgentPrismAdmin", static policy => policy.RequireAssertion(static _ => true)));

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/agents", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --- Meta ucunun muafiyeti ---

    [Fact]
    public async Task Meta_ucu_token_istenirken_bile_aciktir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/meta", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Meta_ucu_uzak_ipten_de_aciktir()
    {
        // Arayuz hangi kimlik yontemini kullanacagini ogrenemezse hicbir zaman
        // oturum acamaz. Bu yuzden meta ucu loopback kisitindan da muaftir.
        await using var host = await AgentPrismTestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/meta");
        request.Headers.Add(AgentPrismTestHost.RemoteIpHeader, "203.0.113.7");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Meta_ucu_basarisiz_policy_altinda_da_aciktir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.RequireAuthorization("AgentPrismAdmin"),
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy("AgentPrismAdmin", static policy => policy.RequireAssertion(static _ => false)));

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/meta", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenAI_uyumlu_uclar_da_korunur()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/v1/responses", UriKind.Relative),
            new { model = "yok", input = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
