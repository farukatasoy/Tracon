using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

// Gerekce RunReplayEndpointTests.cs'deki ile aynidir (K-269): paketteki
// AgentPrism.Testing.AgentPrismTestHost ile bu projenin kendi AgentPrismTestHost'u
// AYNI ada sahiptir; blanket `using AgentPrism.Testing;` CS0104 verirdi.
using FakeModelProvider = AgentPrism.Testing.FakeModelProvider;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>Asenkron onay kutusu uclarinin testleri (Faz 55).</summary>
public sealed class ApprovalEndpointTests
{
    private const string AgentName = "onay-agent";
    private const string TenantHeader = "X-AgentPrism-Tenant";

    private static readonly Uri Run = new($"/agentprism/api/agents/{AgentName}/run", UriKind.Relative);
    private static readonly Uri PendingApprovals = new("/agentprism/api/approvals/pending", UriKind.Relative);

    private static async Task<HttpResponseMessage> PostQueuedAsync(AgentPrismTestHost host, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run) { Content = JsonContent.Create(body) };
        request.Headers.Add("Prefer", "respond-async");

        return await host.Client.SendAsync(request).ConfigureAwait(false);
    }

    private static void ConfigureApprovalAgent(IAgentPrismBuilder builder)
    {
        builder
            .AddModelProvider(new FakeModelProvider("onay-model")
                .CallsTool("cancel_order", new { orderId = "ORD-7" })
                .EchoesLastToolResult())
            .AddTool(
                (Func<string, string>)(orderId => $"{orderId} iptal edildi."),
                name: "cancel_order",
                description: "Bir siparisi iptal eder.",
                requiresApproval: true)
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Kisa yanit ver.",
                Model = new ModelBinding { Provider = "onay-model", Model = "onay-1" },
                ToolNames = ["cancel_order"],
            });
    }

    /// <summary>
    /// Bir calistirma beklenen duruma gelene kadar yoklar.
    /// </summary>
    /// <remarks>
    /// 🚨 Sure dolarsa BURADA patlar. Onceki hâli son gordugu durumu sessizce
    /// dondururdu; cagiran onu denetlemedigi yerlerde (bkz. satir 140, 192) test
    /// devam eder ve ILGISIZ bir iddiada ("bekleyen onay listesi bos") patlardi.
    /// Tam cozum kosumunda olculdu: 16 test projesi paralel kosarken 5 sn yetmiyor,
    /// tek basina 447/447 gecen paket toplu kosumda 1 hata veriyordu. Sure 30 sn'ye
    /// cikarildi (yuk altinda genis, saglikli bir kosumda yine milisaniyeler surer).
    /// </remarks>
    private static async Task<string> WaitForStatusAsync(AgentPrismTestHost host, Guid runId, string expected)
    {
        var uri = new Uri($"/agentprism/api/runs/{runId}", UriKind.Relative);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        string? status = null;

        while (DateTime.UtcNow < deadline)
        {
            using var poll = await host.Client.GetAsync(uri);
            status = (await AgentPrismTestHost.ReadJsonAsync(poll)).GetProperty("status").GetString();

            if (string.Equals(status, expected, StringComparison.Ordinal))
            {
                // `expected` null degildir; esitlik saglandiysa `status` da degildir.
                return status!;
            }

            await Task.Delay(20);
        }

        throw new InvalidOperationException(
            $"Calistirma {runId} 30 saniyede '{expected}' durumuna gelmedi; son gorulen durum: '{status}'.");
    }

    [Fact]
    public async Task Kuyruga_alinan_calistirma_onay_ister_konsoldan_onaylanir_ve_tamamlanir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            ConfigureApprovalAgent,
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "siparisi iptal et", SessionId = "oturum-1" });

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var originalRunId = (await AgentPrismTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        (await WaitForStatusAsync(host, originalRunId, "AwaitingApproval")).ShouldBe("AwaitingApproval");

        using var pendingResponse = await host.Client.GetAsync(PendingApprovals);
        var pending = (await AgentPrismTestHost.ReadJsonAsync(pendingResponse)).EnumerateArray().ShouldHaveSingleItem();

        pending.GetProperty("toolName").GetString().ShouldBe("cancel_order");
        pending.GetProperty("runId").GetGuid().ShouldBe(originalRunId);
        pending.GetProperty("status").GetString().ShouldBe("Pending");

        var approvalId = pending.GetProperty("id").GetGuid();

        using var decideResponse = await host.Client.PostAsJsonAsync(
            new Uri($"/agentprism/api/approvals/{approvalId}/decide", UriKind.Relative),
            new ApprovalDecisionRequest { Approved = true });

        decideResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var decided = await AgentPrismTestHost.ReadJsonAsync(decideResponse);
        decided.GetProperty("status").GetString().ShouldBe("Approved");
        decided.GetProperty("decidedBy").GetString().ShouldNotBeNullOrEmpty();

        // Eski calistirma AwaitingApproval olarak KALIR (K-014); yeni bir
        // calistirma ayni oturumla surer ve Completed olur.
        (await WaitForStatusAsync(host, originalRunId, "AwaitingApproval")).ShouldBe("AwaitingApproval");

        using var runningJobs = await host.Client.GetAsync(new Uri("/agentprism/api/runs?sessionId=oturum-1", UriKind.Relative));
        var runs = (await AgentPrismTestHost.ReadJsonAsync(runningJobs)).EnumerateArray().ToList();

        runs.Count.ShouldBe(2);

        var resumedRunId = runs
            .Select(static run => run.GetProperty("id").GetGuid())
            .Single(id => id != originalRunId);

        (await WaitForStatusAsync(host, resumedRunId, "Completed")).ShouldBe("Completed");

        using var finalRun = await host.Client.GetAsync(new Uri($"/agentprism/api/runs/{resumedRunId}", UriKind.Relative));
        var finalBody = await AgentPrismTestHost.ReadJsonAsync(finalRun);

        finalBody.GetProperty("status").GetString().ShouldBe("Completed");

        using var pendingAfter = await host.Client.GetAsync(PendingApprovals);
        (await AgentPrismTestHost.ReadJsonAsync(pendingAfter)).EnumerateArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Ayni_onaya_ikinci_karar_409_alir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            ConfigureApprovalAgent,
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "siparisi iptal et", SessionId = "oturum-2" });
        var runId = (await AgentPrismTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        await WaitForStatusAsync(host, runId, "AwaitingApproval");

        using var pendingResponse = await host.Client.GetAsync(PendingApprovals);
        var approvalId = (await AgentPrismTestHost.ReadJsonAsync(pendingResponse))
            .EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid();

        var decideUri = new Uri($"/agentprism/api/approvals/{approvalId}/decide", UriKind.Relative);

        using (var first = await host.Client.PostAsJsonAsync(decideUri, new ApprovalDecisionRequest { Approved = true }))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var second = await host.Client.PostAsJsonAsync(decideUri, new ApprovalDecisionRequest { Approved = false });

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Reader_rolu_karar_veremez()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            ConfigureApprovalAgent,
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Operator, static policy => policy.RequireAssertion(static _ => false)));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/agentprism/api/approvals/{Guid.NewGuid()}/decide", UriKind.Relative),
            new ApprovalDecisionRequest { Approved = true });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Baska_kiracinin_onayi_gorunmez()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder =>
            {
                ConfigureApprovalAgent(builder);
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
            },
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "siparisi iptal et", SessionId = "oturum-3" });
        var runId = (await AgentPrismTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        await WaitForStatusAsync(host, runId, "AwaitingApproval");

        using var pendingResponse = await host.Client.GetAsync(PendingApprovals);
        var approvalId = (await AgentPrismTestHost.ReadJsonAsync(pendingResponse))
            .EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid();

        using var otherTenantGet = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"/agentprism/api/approvals/{approvalId}", UriKind.Relative));
        otherTenantGet.Headers.Add(TenantHeader, "baska-kiraci");

        using var getResponse = await host.Client.SendAsync(otherTenantGet);

        getResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var otherTenantDecide = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/agentprism/api/approvals/{approvalId}/decide", UriKind.Relative))
        {
            Content = JsonContent.Create(new ApprovalDecisionRequest { Approved = true }),
        };
        otherTenantDecide.Headers.Add(TenantHeader, "baska-kiraci");

        using var decideResponse = await host.Client.SendAsync(otherTenantDecide);

        decideResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// HATA-S2-004/MT-MCP-023: onceden yalniz kuyruktan kosan (<c>Prefer:
    /// respond-async</c>) calistirmalar bu durumu yansitiyordu; senkron/akissiz
    /// yol ayni onay bekleyen tool cagrisini sessizce <c>Completed</c> olarak
    /// kapatiyordu (bkz. RunRecordingAgent.RunCoreAsync).
    /// </summary>
    [Fact]
    public async Task Senkron_akissiz_calistirma_onay_isteyince_AwaitingApproval_ile_kapanir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(ConfigureApprovalAgent);

        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "siparisi iptal et", SessionId = "oturum-senkron-akissiz" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var runId = (await AgentPrismTestHost.ReadJsonAsync(response)).GetProperty("runId").GetGuid();

        using var runResponse = await host.Client.GetAsync(new Uri($"/agentprism/api/runs/{runId}", UriKind.Relative));
        (await AgentPrismTestHost.ReadJsonAsync(runResponse)).GetProperty("status").GetString()
            .ShouldBe("AwaitingApproval");
    }

    /// <summary>Ayni kusurun akisli (SSE) varyanti — RunRecordingAgent.RunCoreStreamingAsync.</summary>
    [Fact]
    public async Task Senkron_akisli_calistirma_onay_isteyince_AwaitingApproval_ile_kapanir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(ConfigureApprovalAgent);

        using var response = await host.Client.PostAsJsonAsync(
            Run,
            new AgentRunRequest { Message = "siparisi iptal et", SessionId = "oturum-senkron-akisli" });

        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");
        await response.Content.ReadAsStringAsync();

        using var runningJobs = await host.Client.GetAsync(
            new Uri("/agentprism/api/runs?sessionId=oturum-senkron-akisli", UriKind.Relative));
        var run = (await AgentPrismTestHost.ReadJsonAsync(runningJobs)).EnumerateArray().ShouldHaveSingleItem();

        run.GetProperty("status").GetString().ShouldBe("AwaitingApproval");
    }
}
