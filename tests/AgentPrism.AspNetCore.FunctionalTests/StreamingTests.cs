using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// SSE akislarini dogrular: deneme calistirmasi ve calistirma olaylari.
/// </summary>
public sealed class StreamingTests
{
    [Fact]
    public async Task Deneme_calistirmasi_guncellemeleri_akitir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await PostRunAsync(host, new AgentRunRequest { Message = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        frames.ShouldContain(static frame => string.Equals(frame.Event, "update", StringComparison.Ordinal));
        frames[^1].Event.ShouldBe("done");
        frames.Where(static frame => string.Equals(frame.Event, "update", StringComparison.Ordinal))
            .Select(static frame => frame.Data)
            .ShouldContain(static data => data.Contains("Echo: merhaba", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deneme_calistirmasi_ters_vekil_arabellegini_kapatir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await PostRunAsync(host, new AgentRunRequest { Message = "merhaba" });

        response.Headers.GetValues("X-Accel-Buffering").ShouldContain(static value => string.Equals(value, "no", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Oturumlu_calistirma_gecmisi_tasir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using (var first = await PostRunAsync(host, new AgentRunRequest { Message = "ilk", SessionId = "s-1" }))
        {
            await SseReader.ReadAllAsync(await first.Content.ReadAsStreamAsync());
        }

        using (var second = await PostRunAsync(host, new AgentRunRequest { Message = "ikinci", SessionId = "s-1" }))
        {
            await SseReader.ReadAllAsync(await second.Content.ReadAsStreamAsync());
        }

        // Ikinci cagrida modele giden mesajlar ilk turu de icermelidir.
        var echo = host.Services.GetServices<IModelProvider>().OfType<EchoModelProvider>().Single();

        echo.LastRequest
            .Select(static message => message.Text)
            .ShouldContain(static text => string.Equals(text, "ilk", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Olmayan_agent_akis_baslamadan_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/yok-boyle/run", UriKind.Relative),
            new AgentRunRequest { Message = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Bos_mesaj_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await PostRunAsync(host, new AgentRunRequest { Message = "   " });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // --- Calistirma olaylari akisi ---

    [Fact]
    public async Task Calistirma_olaylari_sirali_akitilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var runId = await RunAndGetRunIdAsync(host);

        using var response = await host.Client.GetAsync(
            new Uri($"/agentprism/api/runs/{runId}/events", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        frames.ShouldNotBeEmpty();
        frames[0].Event.ShouldBe("run.started");
        frames[^1].Event.ShouldBe("run.completed");

        // Sira numaralari bosluksuz ve artan olmalidir.
        var ids = frames.Select(static frame => long.Parse(frame.Id!, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        ids.ShouldBe(Enumerable.Range(0, ids.Count).Select(static i => (long)i).ToList());
    }

    [Fact]
    public async Task Last_Event_ID_ile_kaldigi_yerden_devam_eder()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var runId = await RunAndGetRunIdAsync(host);

        List<SseFrame> all;

        using (var full = await host.Client.GetAsync(
            new Uri($"/agentprism/api/runs/{runId}/events", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead))
        {
            all = await SseReader.ReadAllAsync(await full.Content.ReadAsStreamAsync());
        }

        all.Count.ShouldBeGreaterThan(2);

        // Istemci 1 numarali olaya kadar aldi; akis 2'den devam etmelidir.
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/agentprism/api/runs/{runId}/events");
        request.Headers.Add("Last-Event-ID", "1");

        using var resumed = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var frames = await SseReader.ReadAllAsync(await resumed.Content.ReadAsStreamAsync());

        frames.Count.ShouldBe(all.Count - 2);
        frames[0].Id.ShouldBe("2");
        frames[^1].Event.ShouldBe(all[^1].Event);
    }

    [Fact]
    public async Task Olmayan_calistirmanin_olaylari_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri($"/agentprism/api/runs/{Guid.NewGuid()}/events", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    private static Task<HttpResponseMessage> PostRunAsync(AgentPrismTestHost host, AgentRunRequest request)
        => host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/kod-agent/run", UriKind.Relative),
            request);

    /// <summary>Bir calistirma yapar ve olusan calistirma kaydinin kimligini dondurur.</summary>
    private static async Task<Guid> RunAndGetRunIdAsync(AgentPrismTestHost host)
    {
        using (var run = await PostRunAsync(host, new AgentRunRequest { Message = "merhaba" }))
        {
            await SseReader.ReadAllAsync(await run.Content.ReadAsStreamAsync());
        }

        using var runs = await host.Client.GetAsync(new Uri("/agentprism/api/runs", UriKind.Relative));
        var json = await AgentPrismTestHost.ReadJsonAsync(runs);

        json.GetArrayLength().ShouldBeGreaterThan(0);

        return json[0].GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Calistirma_kaydi_ozette_sayilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        await RunAndGetRunIdAsync(host);

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/stats", UriKind.Relative));
        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        json.GetProperty("totalRuns").GetInt64().ShouldBe(1);
        json.GetProperty("completedRuns").GetInt64().ShouldBe(1);
        json.GetProperty("errorRate").GetDouble().ShouldBe(0);
        json.GetProperty("byAgent")[0].GetProperty("agentName").GetString().ShouldBe("kod-agent");
    }

    [Fact]
    public async Task Bos_depoda_ozet_sifir_ve_hata_orani_bos_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/stats", UriKind.Relative));
        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        json.GetProperty("totalRuns").GetInt64().ShouldBe(0);
        json.GetProperty("errorRate").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
