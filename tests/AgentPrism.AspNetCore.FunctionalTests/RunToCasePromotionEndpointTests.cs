using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// <c>POST /api/evals/{name}/cases/from-run/{runId}</c> uctan uca testleri
/// (Faz 45, F-53).
/// </summary>
public sealed class RunToCasePromotionEndpointTests
{
    private const string TenantHeader = "X-AgentPrism-Tenant";

    [Fact]
    public async Task Basarili_calistirma_referans_olarak_terfi_edilir_ve_denetim_kaydi_yazilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var runId = await RunAsync(host, "merhaba", "oturum-1");
        await SaveSuiteAsync(host, "kod-agent");

        using var response = await host.Client.PostAsync(PromoteUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("query").GetString().ShouldBe("merhaba");
        body.GetProperty("expectedOutput").GetString()!.ShouldContain("Echo: merhaba");
        body.GetProperty("sourceKind").GetString().ShouldBe("ReferenceRun");
        body.GetProperty("sourceRunId").GetGuid().ShouldBe(runId);
        body.GetProperty("seq").GetInt32().ShouldBe(0);

        var auditLog = host.Services.GetRequiredService<IAuditLog>();
        var entries = await auditLog.QueryAsync(new AuditQuery { TenantId = "default", Action = "eval.case.promoted" });
        entries.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Ayni_calistirma_ikinci_kez_terfi_edilirse_200_doner_ve_ikinci_vaka_olusmaz()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var runId = await RunAsync(host, "merhaba", "oturum-2");
        await SaveSuiteAsync(host, "kod-agent");

        using var first = await host.Client.PostAsync(PromoteUri(runId), content: null);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var firstCaseId = (await AgentPrismTestHost.ReadJsonAsync(first)).GetProperty("id").GetGuid();

        using var second = await host.Client.PostAsync(PromoteUri(runId), content: null);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondCaseId = (await AgentPrismTestHost.ReadJsonAsync(second)).GetProperty("id").GetGuid();

        secondCaseId.ShouldBe(firstCaseId);

        using var cases = await host.Client.GetAsync(CasesUri);
        (await AgentPrismTestHost.ReadJsonAsync(cases)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Basarisiz_calistirma_terfi_edilir_ve_expectedOutput_bostur()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder
                .AddModelProvider(new ThrowingModelProvider())
                .AddAgent(TestData.Definition(name: "kirik-agent") with
                {
                    Model = new ModelBinding { Provider = "throws", Model = "throws-1" },
                }));

        var runId = await RunAsync(host, "merhaba", "oturum-3", agentName: "kirik-agent", expectFailure: true);
        await SaveSuiteAsync(host, "kirik-agent");

        using var response = await host.Client.PostAsync(PromoteUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("sourceKind").GetString().ShouldBe("FailedRun");
        body.GetProperty("expectedOutput").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Olumsuz_puanli_calistirma_terfi_edilir_ve_expectedOutput_bostur()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var runId = await RunAsync(host, "merhaba", "oturum-4");
        await SaveSuiteAsync(host, "kod-agent");

        using (var feedback = await host.Client.PostAsJsonAsync(
            new Uri($"/agentprism/api/runs/{runId}/feedback", UriKind.Relative),
            new { kind = "Binary", value = 0 }))
        {
            feedback.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await host.Client.PostAsync(PromoteUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("sourceKind").GetString().ShouldBe("NegativeScore");
        body.GetProperty("expectedOutput").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Cok_turlu_calistirma_409_ile_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        await RunAsync(host, "birinci tur", "oturum-5");
        var secondRunId = await RunAsync(host, "ikinci tur", "oturum-5");
        await SaveSuiteAsync(host, "kod-agent");

        using var response = await host.Client.PostAsync(PromoteUri(secondRunId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Oturumsuz_calistirma_da_terfi_edilebilir()
    {
        // Sorgu run_events'ten (RunStarted.Text) okunur, oturumdan degil (Faz 45):
        // oturumsuz (sessionId verilmeyen) bir calistirma da terfi edilebilmelidir.
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var runId = await RunAsync(host, "merhaba", sessionId: null);
        await SaveSuiteAsync(host, "kod-agent");

        using var response = await host.Client.PostAsync(PromoteUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("query").GetString().ShouldBe("merhaba");
    }

    [Fact]
    public async Task Olay_akisi_bos_calistirma_sorgusuz_kabul_edilir_422_doner()
    {
        // RunStarted olayi olmadan (elle tohumlanan eski/bozuk bir kayit) query
        // okunamaz; bu, NoQuery yolunun hala erisilebilir oldugunu dogrular.
        await using var host = await AgentPrismTestHost.StartAsync();
        await SaveSuiteAsync(host, "test-agent");

        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = AgentPrismId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
        });

        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        using var response = await host.Client.PostAsync(PromoteUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Baska_kiracinin_calistirmasi_terfi_edilemez_AYNI_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder =>
            {
                builder.AddAgent(TestData.Definition());
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
            });

        var runId = await RunAsAsync(host, "merhaba", "oturum-6", "kiraci-a");
        await SaveSuiteAsAsync(host, "kod-agent", "kiraci-a");
        await SaveSuiteAsAsync(host, "kod-agent", "kiraci-b");

        using var missing = await SendAsTenant(host, PromoteUri(AgentPrismId.NewId()), "kiraci-b");
        using var wrongTenant = await SendAsTenant(host, PromoteUri(runId), "kiraci-b");

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        wrongTenant.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var ownTenant = await SendAsTenant(host, PromoteUri(runId), "kiraci-a");
        ownTenant.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Ne_basarisiz_ne_tamamlanmis_calistirma_acik_sourceKind_ister()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        await SaveSuiteAsync(host, "test-agent");

        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = AgentPrismId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
        });

        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Canceled,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        using var response = await host.Client.PostAsync(PromoteUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Olmayan_takim_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var runId = await RunAsync(host, "merhaba", "oturum-7");

        using var response = await host.Client.PostAsync(
            new Uri("/agentprism/api/evals/yok-boyle/cases/from-run/" + runId, UriKind.Relative),
            content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static readonly Uri CasesUri = new("/agentprism/api/evals/musteri-destek-takimi/cases", UriKind.Relative);

    private static Uri PromoteUri(Guid runId)
        => new($"/agentprism/api/evals/musteri-destek-takimi/cases/from-run/{runId}", UriKind.Relative);

    private static async Task SaveSuiteAsync(AgentPrismTestHost host, string agentName)
    {
        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/evals/musteri-destek-takimi", UriKind.Relative),
            new EvalSuiteSaveRequest
            {
                AgentName = agentName,
                Checks = System.Text.Json.JsonDocument.Parse("""[{"kind":"nonEmpty"}]""").RootElement,
            });

        response.EnsureSuccessStatusCode();
    }

    private static async Task SaveSuiteAsAsync(AgentPrismTestHost host, string agentName, string tenant)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put, new Uri("/agentprism/api/evals/musteri-destek-takimi", UriKind.Relative));
        request.Headers.Add(TenantHeader, tenant);
        request.Content = JsonContent.Create(new EvalSuiteSaveRequest
        {
            AgentName = agentName,
            Checks = System.Text.Json.JsonDocument.Parse("""[{"kind":"nonEmpty"}]""").RootElement,
        });

        using var response = await host.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Bir calistirma yapar ve olusan calistirma kaydinin kimligini dondurur.</summary>
    private static async Task<Guid> RunAsync(
        AgentPrismTestHost host,
        string message,
        string? sessionId,
        string agentName = "kod-agent",
        bool expectFailure = false)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/agentprism/api/agents/{agentName}/run", UriKind.Relative),
            new AgentRunRequest { Message = message, SessionId = sessionId });

        response.EnsureSuccessStatusCode();

        if (expectFailure)
        {
            // Hatalar da SSE akisinda bir olay olarak tasinir; akisin tamami
            // okunmalidir ki calistirma kapansin.
            try
            {
                await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
            }
            catch
            {
                // Beklenen: akis bir hata olayiyla kapanir.
            }
        }
        else
        {
            await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        }

        using var runs = await host.Client.GetAsync(new Uri("/agentprism/api/runs", UriKind.Relative));
        var json = await AgentPrismTestHost.ReadJsonAsync(runs);

        json.GetArrayLength().ShouldBeGreaterThan(0);

        return json[0].GetProperty("id").GetGuid();
    }

    private static async Task<Guid> RunAsAsync(AgentPrismTestHost host, string message, string sessionId, string tenant)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/agentprism/api/agents/kod-agent/run");
        request.Headers.Add(TenantHeader, tenant);
        request.Content = JsonContent.Create(new AgentRunRequest { Message = message, SessionId = sessionId });

        using (var response = await host.Client.SendAsync(request))
        {
            response.EnsureSuccessStatusCode();
            await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        }

        var runsRequest = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/runs");
        runsRequest.Headers.Add(TenantHeader, tenant);

        using var runs = await host.Client.SendAsync(runsRequest);
        var json = await AgentPrismTestHost.ReadJsonAsync(runs);

        return json[0].GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> SendAsTenant(AgentPrismTestHost host, Uri uri, string tenant)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Add(TenantHeader, tenant);

        return await host.Client.SendAsync(request);
    }

    /// <summary>Her cagriya <see cref="InvalidOperationException"/> firlatan sohbet istemcisi.</summary>
    private sealed class ThrowingModelProvider : IModelProvider
    {
        public string Name => "throws";

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "throws-1" }];

        public IChatClient CreateChatClient(ModelBinding binding) => new ThrowingChatClient();

        private sealed class ThrowingChatClient : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("test: model kasitli olarak basarisiz oluyor.");

            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("test: model kasitli olarak basarisiz oluyor.");

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
            }
        }
    }
}
