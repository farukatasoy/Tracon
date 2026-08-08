using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Bearer token'in AgentPrism'in disariya verdigi hicbir ciktida gorunmedigini
/// dogrular.
/// </summary>
/// <remarks>
/// Korunan sinir sudur: token yalnizca gelen istegin basligiyla karsilastirilir.
/// Ayar nesnesi, <c>/api/meta</c> ciktisi, hata yanitlari ve gunluk satirlari
/// token'i <strong>hicbir kosulda</strong> tasiyamaz.
/// </remarks>
public sealed class SecretLeakTests
{
    private const string Token = "cok-gizli-token-DENEME-91af3c";

    [Fact]
    public void Ayar_nesnesi_kendi_ToString_metodunu_tanimlamaz()
    {
        // Ayar nesnesi record OLMAMALIDIR: derleyicinin urettigi ToString tum
        // ozellikleri yazar ve token'i ilk gunluk satirinda ifsa ederdi.
        typeof(AgentPrismEndpointOptions).GetMethod(nameof(ToString), Type.EmptyTypes)!
            .DeclaringType.ShouldBe(typeof(object));
    }

    [Fact]
    public async Task Meta_ciktisi_token_icermez()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/meta", UriKind.Relative));

        (await response.Content.ReadAsStringAsync()).ShouldNotContain(Token);
    }

    [Fact]
    public async Task Reddedilen_istegin_yaniti_token_icermez()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "yanlis");

        using var response = await host.Client.SendAsync(request);

        (await response.Content.ReadAsStringAsync()).ShouldNotContain(Token);
    }

    [Fact]
    public async Task Gunluk_satirlari_token_icermez()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using (var rejected = await host.Client.GetAsync(new Uri("/agentprism/api/agents", UriKind.Relative)))
        {
            rejected.Dispose();
        }

        using var accepted = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        accepted.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        using (var response = await host.Client.SendAsync(accepted))
        {
            response.EnsureSuccessStatusCode();
        }

        host.Logs.AllText.ShouldNotContain(Token);
    }

    [Fact]
    public async Task Kod_agentinin_calistirilmasi_token_sizdirmaz()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            static options =>
            {
                options.AuthToken = Token;
                options.AllowRemoteAccess = true;
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/agentprism/api/agents/kod-agent/run")
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "merhaba", SessionId = "s-gizli" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        using (var response = await host.Client.SendAsync(request))
        {
            response.EnsureSuccessStatusCode();
            (await response.Content.ReadAsStringAsync()).ShouldNotContain(Token);
        }

        // Oturum durumu, calistirma kayitlari ve gunlukler de temiz olmalidir.
        using var sessionRequest = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/sessions/s-gizli");
        sessionRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        using (var session = await host.Client.SendAsync(sessionRequest))
        {
            (await session.Content.ReadAsStringAsync()).ShouldNotContain(Token);
        }

        using var runsRequest = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/runs");
        runsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        using (var runs = await host.Client.SendAsync(runsRequest))
        {
            (await runs.Content.ReadAsStringAsync()).ShouldNotContain(Token);
        }

        host.Logs.AllText.ShouldNotContain(Token);
    }

    // --- Faz 53: API anahtari ---

    [Fact]
    public async Task Ham_api_anahtari_listelemede_gorunmez()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "ci", "AgentsRead");

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/api-keys", UriKind.Relative));

        (await response.Content.ReadAsStringAsync()).ShouldNotContain(created.PlaintextKey);
    }

    [Fact]
    public async Task Ham_api_anahtari_denetim_izinde_gorunmez()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "ci", "AgentsRead");

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/audit", UriKind.Relative));

        (await response.Content.ReadAsStringAsync()).ShouldNotContain(created.PlaintextKey);
    }

    [Fact]
    public async Task Ham_api_anahtari_reddedilen_istegin_yanitinda_gorunmez()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "readonly", "RunsRead");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/agentprism/api/agents/kod-agent/run")
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "merhaba" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain(created.PlaintextKey);
    }

    [Fact]
    public async Task Ham_api_anahtari_gunluk_satirlarinda_gorunmez()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "ci", "AgentsRead");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using (var response = await host.Client.SendAsync(request))
        {
            response.EnsureSuccessStatusCode();
        }

        host.Logs.AllText.ShouldNotContain(created.PlaintextKey);
    }
}
