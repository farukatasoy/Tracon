using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Aile F'nin dort yeni kapsaminin (<c>PlatformRead</c>, <c>PlatformAdmin</c>,
/// <c>SecurityAdmin</c>, <c>AuditRead</c>) uclara dogru baglandigini ve yetki
/// uzatma (attenuation) sinirini nokta ornekleriyle dogrular. Kapsam listesinin
/// TAMAMININ eslendigini <see cref="ApiKeyScopeCoverageTests"/> denetler; bu
/// dosya "dogru kapsam gecer, yanlis kapsam gecmez" davranisina odaklanir.
/// </summary>
public sealed class ApiKeyScopeEnforcementTests
{
    [Fact]
    public async Task PlatformRead_kapsamli_anahtar_zamanlamalari_listeler()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "platform-reader", "PlatformRead");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/schedules");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PlatformRead_kapsami_zamanlama_yazmaya_yetmez()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "platform-reader", "PlatformRead");

        using var request = new HttpRequestMessage(HttpMethod.Put, "/agentprism/api/schedules/gunluk")
        {
            Content = JsonContent.Create(new { kind = "AgentBatch", targetName = "kod-agent", timeZone = "UTC", enabled = true }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PlatformAdmin_kapsamli_anahtar_zamanlama_yazar()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "platform-admin", "PlatformAdmin");

        using var request = new HttpRequestMessage(HttpMethod.Put, "/agentprism/api/schedules/gunluk")
        {
            Content = JsonContent.Create(
                new { kind = "AgentBatch", targetName = "kod-agent", timeZone = "UTC", payload = new { }, enabled = true }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SecurityAdmin_kapsamli_anahtar_anahtar_listesine_erisir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "security-admin", "SecurityAdmin");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/api-keys");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AuditRead_kapsamli_anahtar_denetim_izini_okur()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "audit-reader", "AuditRead");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/audit");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RunsRead_denetim_izini_okuyamaz()
    {
        // Yeniden kullanim regresyonu (§7.3.3): AuditRead ayri bir kapsamdir,
        // RunsRead'e sessizce dusmemelidir.
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "runs-reader", "RunsRead");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/audit");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RunsRead_istatistik_okur_ama_maliyeti_yeniden_hesaplayamaz()
    {
        // §7.3.3: /api/stats RunsRead, /api/stats/recalculate-costs RunsWrite —
        // ayni dosyada komsu iki uc farkli kapsam gerektirir.
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "runs-reader", "RunsRead");

        using var read = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/stats");
        read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);
        using var readResponse = await host.Client.SendAsync(read);
        readResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var write = new HttpRequestMessage(HttpMethod.Post, "/agentprism/api/stats/recalculate-costs");
        write.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);
        using var writeResponse = await host.Client.SendAsync(write);
        writeResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PlatformAdmin_kapsamli_anahtar_yeni_anahtar_ureten_uca_erisemez()
    {
        // §7.3.2: yukselme kapisi — /api/api-keys SecurityAdmin gerektirir,
        // PlatformAdmin bunu kapsamaz.
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "platform-admin", "PlatformAdmin");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/agentprism/api/api-keys")
        {
            Content = JsonContent.Create(new { name = "ikinci", scopes = new[] { "PlatformAdmin" } }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Yetki uzatma / attenuation (bolum 53.3) ---

    [Fact]
    public async Task SecurityAdmin_anahtari_kendi_tasimadigi_kapsami_uretemez()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "security-only", "SecurityAdmin");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/agentprism/api/api-keys")
        {
            // Caller yalniz SecurityAdmin tasir; RunsWrite istemek reddedilmelidir.
            Content = JsonContent.Create(new { name = "genisletilmis", scopes = new[] { "SecurityAdmin", "RunsWrite" } }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SecurityAdmin_anahtari_kendi_tasidigi_kapsami_uretebilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "security-and-runs", "SecurityAdmin", "RunsWrite");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/agentprism/api/api-keys")
        {
            Content = JsonContent.Create(new { name = "alt-kume", scopes = new[] { "RunsWrite" } }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Statik_token_ile_gelen_istek_attenuation_disidir()
    {
        // Statik AuthToken/kullanici kimligiyle gelen istek bir ApiKeyRecord
        // TASIMAZ (ApiKeyRequestContext.Get() null doner); attenuation
        // yalniz istegi dogrulayan bir API anahtari varsa uygulanir (53.3).
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/agentprism/api/api-keys",
            new { name = "kok", scopes = new[] { "SecurityAdmin", "PlatformAdmin", "AuditRead" } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
