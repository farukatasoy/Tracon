using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary><c>Prefer: respond-async</c> destegi testleri (Faz 46).</summary>
public sealed class AsyncRunTests
{
    private const string PreferHeaderName = "Prefer";

    private static readonly Uri Run = new("/agentprism/api/agents/kod-agent/run", UriKind.Relative);
    private static readonly Uri Quotas = new("/agentprism/api/quotas", UriKind.Relative);

    private static async Task<HttpResponseMessage> PostAsyncAsync(AgentPrismTestHost host, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run) { Content = JsonContent.Create(body) };
        request.Headers.Add(PreferHeaderName, "respond-async");

        return await host.Client.SendAsync(request).ConfigureAwait(false);
    }

    [Fact]
    public async Task Kuyruga_alinir_202_Location_ve_PreferenceApplied_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        using var response = await PostAsyncAsync(host, new AgentRunRequest { Message = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Headers.GetValues("Preference-Applied").ShouldContain("respond-async", StringComparer.Ordinal);
        response.Headers.Location.ShouldNotBeNull();

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        var runId = body.GetProperty("runId").GetGuid();
        body.GetProperty("jobId").GetGuid().ShouldBe(runId);
        response.Headers.Location!.OriginalString.ShouldEndWith($"/api/runs/{runId}");
    }

    [Fact]
    public async Task Kuyruga_alindiktan_hemen_sonra_GET_404_degil_Queued_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        using var accepted = await PostAsyncAsync(host, new AgentRunRequest { Message = "merhaba" });
        var runId = (await AgentPrismTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        using var run = await host.Client.GetAsync(new Uri($"/agentprism/api/runs/{runId}", UriKind.Relative));

        run.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AgentPrismTestHost.ReadJsonAsync(run)).GetProperty("status").GetString().ShouldBe("Queued");
    }

    [Fact]
    public async Task Is_isci_tarafindan_alinip_Completed_olur()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        using var accepted = await PostAsyncAsync(host, new AgentRunRequest { Message = "merhaba" });
        var runId = (await AgentPrismTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        var uri = new Uri($"/agentprism/api/runs/{runId}", UriKind.Relative);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        string? status = null;

        while (DateTime.UtcNow < deadline)
        {
            using var poll = await host.Client.GetAsync(uri);
            status = (await AgentPrismTestHost.ReadJsonAsync(poll)).GetProperty("status").GetString();

            if (!string.Equals(status, "Queued", StringComparison.Ordinal) &&
                !string.Equals(status, "Running", StringComparison.Ordinal))
            {
                break;
            }

            await Task.Delay(20);
        }

        status.ShouldBe("Completed");
    }

    [Fact]
    public async Task Baslik_yokken_SSE_davranisi_degismez()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");
        response.Headers.Contains("Preference-Applied").ShouldBeFalse();
    }

    [Fact]
    public async Task Kapaliyken_baslik_tasiyan_istek_501_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.Configure<AgentPrismAsyncRunOptions>(
                static options => options.Enabled = false));

        using var response = await PostAsyncAsync(host, new AgentRunRequest { Message = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task Kota_dolu_iken_kuyruga_alma_429_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        using (var created = await host.Client.PutAsJsonAsync(
                   Quotas,
                   new QuotaSaveRequest { AgentName = "kod-agent", Period = QuotaPeriod.Daily, MaxRuns = 1, Enabled = true }))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // Bos bir donemde kullanim kaydi hic YOKTUR (Allowed doner); kotanin
        // gercekten devreye girmesi icin once BIR calistirma tuketilmelidir —
        // sonraki istek (kuyruga alma dahil) o zaman 429 alir.
        using (var first = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "merhaba" }))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await PostAsyncAsync(host, new AgentRunRequest { Message = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Bos_mesaj_400_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        using var response = await PostAsyncAsync(host, new AgentRunRequest { Message = "  " });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Onay_karari_destelenmez_400_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        using var response = await PostAsyncAsync(
            host,
            new AgentRunRequest
            {
                SessionId = "oturum-1",
                Approvals = [new ToolApprovalDecision { RequestId = "istek-1", Approved = true }],
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Kuyruktaki_calistirma_iptal_edilebilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        using var accepted = await PostAsyncAsync(host, new AgentRunRequest { Message = "merhaba" });
        var runId = (await AgentPrismTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        using var cancelled = await host.Client.PostAsync(
            new Uri($"/agentprism/api/runs/{runId}/cancel", UriKind.Relative), content: null);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        using var run = await host.Client.GetAsync(new Uri($"/agentprism/api/runs/{runId}", UriKind.Relative));
        (await AgentPrismTestHost.ReadJsonAsync(run)).GetProperty("status").GetString().ShouldBe("Canceled");

        using var secondCancel = await host.Client.PostAsync(
            new Uri($"/agentprism/api/runs/{runId}/cancel", UriKind.Relative), content: null);
        secondCancel.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
