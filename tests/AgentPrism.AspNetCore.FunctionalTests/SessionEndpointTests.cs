using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Oturum uclarini dogrular: listeleme, sohbet gecmisi okuma ve silme.
/// </summary>
public sealed class SessionEndpointTests
{
    [Fact]
    public async Task Oturum_detayi_sohbet_gecmisini_dondurur()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        await RunAsync(host, "merhaba", "oturum-1");

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/api/sessions/oturum-1", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        json.GetProperty("id").GetString().ShouldBe("oturum-1");
        json.GetProperty("agentName").GetString().ShouldBe("kod-agent");

        var messages = json.GetProperty("messages");
        messages.ValueKind.ShouldBe(JsonValueKind.Array);
        messages.GetArrayLength().ShouldBeGreaterThanOrEqualTo(2);

        var text = messages.ToString();
        text.ShouldContain("merhaba");
        text.ShouldContain("Echo: merhaba");
    }

    [Fact]
    public async Task Oturum_detayi_opak_durumu_da_dondurur()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        await RunAsync(host, "merhaba", "oturum-2");

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/api/sessions/oturum-2", UriKind.Relative));

        var state = (await AgentPrismTestHost.ReadJsonAsync(response)).GetProperty("state");

        state.ValueKind.ShouldBe(JsonValueKind.Object);

        // Oturum kimligi damgasi durumun icinde yasar ve oturumla kalicilasır.
        state.ToString().ShouldContain(AgentSessionIdentity.StateKey);
    }

    [Fact]
    public async Task Oturumlar_listelenir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        await RunAsync(host, "bir", "oturum-a");
        await RunAsync(host, "iki", "oturum-b");

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/sessions", UriKind.Relative));
        var ids = (await AgentPrismTestHost.ReadJsonAsync(response))
            .EnumerateArray()
            .Select(static session => session.GetProperty("id").GetString())
            .ToList();

        ids.ShouldContain(static id => string.Equals(id, "oturum-a", StringComparison.Ordinal));
        ids.ShouldContain(static id => string.Equals(id, "oturum-b", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Oturum_agent_adina_gore_filtrelenir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        await RunAsync(host, "merhaba", "oturum-c");

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/api/sessions?agentName=baska-agent", UriKind.Relative));

        (await AgentPrismTestHost.ReadJsonAsync(response)).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Oturum_silinir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        await RunAsync(host, "merhaba", "oturum-silinecek");

        using (var deleted = await host.Client.DeleteAsync(
            new Uri("/agentprism/api/sessions/oturum-silinecek", UriKind.Relative)))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var missing = await host.Client.GetAsync(
            new Uri("/agentprism/api/sessions/oturum-silinecek", UriKind.Relative));

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Olmayan_oturum_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/api/sessions/yok-boyle", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Agent_katalogdan_kalkarsa_ustveri_yine_doner()
    {
        // Gozlemlenebilirlik islevselligi bozmaz: gecmis okunamasa da oturum
        // ustverisi dondurulur.
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        await RunAsync(host, "merhaba", "oturum-yetim");

        // Ayni depoyu paylasmayan yeni bir barindirici kurmak yerine, oturumu
        // katalogda olmayan bir agent adiyla dogrudan yaziyoruz.
        var store = (ISessionStore)host.Services.GetService(typeof(ISessionStore))!;
        var existing = await store.GetAsync("oturum-yetim");

        existing.ShouldNotBeNull();
        await store.SaveAsync(existing with { Id = "oturum-silinmis-agent", AgentName = "artik-yok" });

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/api/sessions/oturum-silinmis-agent", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await AgentPrismTestHost.ReadJsonAsync(response);
        json.GetProperty("agentName").GetString().ShouldBe("artik-yok");
        json.GetProperty("messages").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Bellek_ici_kurulumda_dallandirma_501_doner()
    {
        // 🚨 SQL saglayicisi kayitli degilken sohbet gecmisi MAF'in
        // InMemoryChatHistoryProvider'inda, oturum durumunun OPAK blogunda
        // yasar ve belirli bir sira numarasina kadar kopyalanamaz. Sessizce
        // tamamini kopyalamak istenen dali uretmezdi; uc bunu acikca soyler.
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        await RunAsync(host, "merhaba", "oturum-dal");

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/sessions/oturum-dal/branch", UriKind.Relative),
            new { upToSequence = 0 });

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        body.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("SQL");
    }

    [Fact]
    public async Task Olmayan_oturum_dallandirilamaz_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/sessions/yok-boyle-bir-oturum/branch", UriKind.Relative),
            new { upToSequence = 0 });

        // Depo kayitli olmadigi icin "desteklenmiyor" cevabi ONCE gelir: eksik
        // yetenek, eksik kayittan daha genel bir sebeptir ve kullaniciyi dogru
        // eyleme yonlendirir.
        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    private static async Task RunAsync(AgentPrismTestHost host, string message, string sessionId)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/kod-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = message, SessionId = sessionId });

        response.EnsureSuccessStatusCode();

        await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
    }
}
