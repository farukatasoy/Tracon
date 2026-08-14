using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Workflow uclari: katalog, tanim yonetimi, calistirma ve kontrol noktalari.
/// </summary>
/// <remarks>
/// Iki kurulum test edilir: motor kayitli (<c>UseWorkflows()</c>) ve kayitsiz.
/// Kayitsiz kurulumda tanim yonetimi calismali, calistirma <c>501</c> donmelidir.
/// </remarks>
public sealed class WorkflowEndpointTests
{
    [Fact]
    public async Task Motor_kayitli_degilse_calistirma_501_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition("yazar")));

        using var response = await host.Client.PostAsJsonAsync(
            "/agentprism/api/workflows/zincir/run",
            new WorkflowRunHttpRequest { Message = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);

        var problem = await AgentPrismTestHost.ReadJsonAsync(response);

        problem.GetProperty("detail").GetString()!.ShouldContain("UseWorkflows()", Case.Sensitive);
    }

    [Fact]
    public async Task Motor_kayitli_degilken_tanim_yonetimi_calisir()
    {
        // Tanim depolari AddAgentPrism() tarafindan her zaman kaydedilir; motor
        // yalnizca YURUTMEYI acar. Boylece bir sorun aninda motor kaldirilsa
        // bile tanimlar okunabilir kalir.
        await using var host = await AgentPrismTestHost.StartAsync();

        using var saved = await SaveAsync(host, "zincir", Sequential());

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var listed = await host.Client.GetAsync("/agentprism/api/workflows");

        listed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(listed);

        body.EnumerateArray().Select(static item => item.GetProperty("name").GetString())
            .ShouldContain(static name => string.Equals(name, "zincir", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tanim_kaydedilir_okunur_ve_silinir()
    {
        await using var host = await StartWithEngineAsync();

        using (var saved = await SaveAsync(host, "zincir", Sequential()))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await AgentPrismTestHost.ReadJsonAsync(saved);

            body.GetProperty("name").GetString().ShouldBe("zincir");
            body.GetProperty("version").GetInt32().ShouldBe(1);

            // Enum'lar kabloda AD olarak yazilir, sayi olarak degil.
            body.GetProperty("kind").GetString().ShouldBe("Sequential");
        }

        using (var read = await host.Client.GetAsync("/agentprism/api/workflows/zincir"))
        {
            read.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var deleted = await host.Client.DeleteAsync("/agentprism/api/workflows/zincir"))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var missing = await host.Client.GetAsync("/agentprism/api/workflows/zincir"))
        {
            missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Gecersiz_tanim_KAYIT_ANINDA_reddedilir()
    {
        await using var host = await StartWithEngineAsync();

        using var response = await SaveAsync(host, "bos", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Sequential,
            AgentNames = [],
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await AgentPrismTestHost.ReadJsonAsync(response);

        problem.GetProperty("detail").GetString()!.ShouldContain("has no agents", Case.Sensitive);
    }

    [Fact]
    public async Task Tekrar_eden_agent_adi_reddedilir()
    {
        await using var host = await StartWithEngineAsync();

        using var response = await SaveAsync(host, "tekrar", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Sequential,
            AgentNames = ["yazar", "yazar"],
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Olmayan_workflow_calistirilamaz()
    {
        await using var host = await StartWithEngineAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/agentprism/api/workflows/olmayan/run",
            new WorkflowRunHttpRequest { Message = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Calistirma_SSE_ile_akar_ve_agac_uretir()
    {
        await using var host = await StartWithEngineAsync();

        using (var saved = await SaveAsync(host, "zincir", Sequential()))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await host.Client.PostAsJsonAsync(
            "/agentprism/api/workflows/zincir/run",
            new WorkflowRunHttpRequest { Message = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        // Ilk cerceve calistirma kimligini bildirir; son cerceve akisin
        // bittigini soyler. Ikisi olmadan istemci baglantinin koptugunu mu
        // yoksa isin bittigini mi anlayamaz.
        frames[0].Event.ShouldBe("run");
        frames[^1].Event.ShouldBe("done");

        var runId = JsonDocument.Parse(frames[0].Data).RootElement.GetProperty("runId").GetGuid();

        // Agac: bir workflow satiri + iki agent satiri.
        using var tree = await host.Client.GetAsync($"/agentprism/api/runs/{runId}/tree");

        tree.StatusCode.ShouldBe(HttpStatusCode.OK);

        var runs = await AgentPrismTestHost.ReadJsonAsync(tree);
        var items = runs.EnumerateArray().ToList();

        items.Count.ShouldBe(3);

        var workflowRun = items.Single(item => string.Equals(
            item.GetProperty("kind").GetString(),
            "Workflow",
            StringComparison.Ordinal));

        workflowRun.GetProperty("workflowName").GetString().ShouldBe("zincir");
        workflowRun.GetProperty("id").GetGuid().ShouldBe(runId);

        var agentRuns = items
            .Where(item => string.Equals(item.GetProperty("kind").GetString(), "Agent", StringComparison.Ordinal))
            .ToList();

        agentRuns.Count.ShouldBe(2);
        agentRuns.ShouldAllBe(item => item.GetProperty("parentRunId").GetGuid() == runId);
    }

    [Fact]
    public async Task Kontrol_noktalari_listelenir_ve_surdurulur()
    {
        await using var host = await StartWithEngineAsync();

        using (var saved = await SaveAsync(host, "zincir", Sequential()))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        Guid runId;

        using (var response = await host.Client.PostAsJsonAsync(
                   "/agentprism/api/workflows/zincir/run",
                   new WorkflowRunHttpRequest { Message = "merhaba" }))
        {
            var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
            runId = JsonDocument.Parse(frames[0].Data).RootElement.GetProperty("runId").GetGuid();
        }

        using (var checkpoints = await host.Client.GetAsync($"/agentprism/api/workflows/runs/{runId}/checkpoints"))
        {
            checkpoints.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await AgentPrismTestHost.ReadJsonAsync(checkpoints);

            body.GetArrayLength().ShouldBeGreaterThan(0);
        }

        using (var resumed = await host.Client.PostAsJsonAsync(
                   $"/agentprism/api/workflows/runs/{runId}/resume",
                   new WorkflowResumeHttpRequest()))
        {
            resumed.StatusCode.ShouldBe(HttpStatusCode.OK);

            var frames = await SseReader.ReadAllAsync(await resumed.Content.ReadAsStreamAsync());

            frames[0].Event.ShouldBe("run");
            frames[^1].Event.ShouldBe("done");
        }
    }

    [Fact]
    public async Task Olmayan_calistirmanin_kontrol_noktalari_404_doner()
    {
        await using var host = await StartWithEngineAsync();

        using var response = await host.Client.GetAsync(
            $"/agentprism/api/workflows/runs/{AgentPrismId.NewId()}/checkpoints");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // --- Faz 16: graf, bekleyen istek, yanit ---

    [Fact]
    public async Task Graf_ucu_dugumleri_kenarlari_ve_mermaid_dondurur()
    {
        await using var host = await StartWithEngineAsync();

        using (var saved = await SaveAsync(host, "zincir", Sequential()))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await host.Client.GetAsync("/agentprism/api/workflows/zincir/graph");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var graph = await AgentPrismTestHost.ReadJsonAsync(response);

        graph.GetProperty("name").GetString().ShouldBe("zincir");
        graph.GetProperty("mermaid").GetString().ShouldNotBeNullOrWhiteSpace();
        graph.GetProperty("startExecutorId").GetString().ShouldNotBeNullOrWhiteSpace();

        var nodes = graph.GetProperty("nodes").EnumerateArray().ToList();
        var edges = graph.GetProperty("edges").EnumerateArray().ToList();

        // Iki agent + hazir desenin ekledigi cikti dugumu.
        nodes.Count.ShouldBeGreaterThanOrEqualTo(3);
        edges.ShouldNotBeEmpty();

        // Enum'lar kabloda AD olarak yazilir (K-040).
        nodes.Select(static node => node.GetProperty("kind").GetString())
            .ShouldContain(static kind => string.Equals(kind, "Agent", StringComparison.Ordinal));

        var agentNames = nodes
            .Where(static node => string.Equals(node.GetProperty("kind").GetString(), "Agent", StringComparison.Ordinal))
            .Select(static node => node.GetProperty("agentName").GetString())
            .Order(StringComparer.Ordinal)
            .ToList();

        agentNames.ShouldBe(["editor", "yazar"]);
    }

    [Fact]
    public async Task Olmayan_workflow_grafi_404_doner()
    {
        await using var host = await StartWithEngineAsync();

        using var response = await host.Client.GetAsync("/agentprism/api/workflows/yok/graph");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Motor_kayitli_degilse_graf_501_doner()
    {
        // Graf DERLENMIS workflow'dan cikarilir; derleyici motorla gelir.
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var saved = await SaveAsync(host, "zincir", Sequential()))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await host.Client.GetAsync("/agentprism/api/workflows/zincir/graph");

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task Plan_onayi_baska_desende_reddedilir()
    {
        await using var host = await StartWithEngineAsync();

        using var response = await SaveAsync(host, "zincir", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Sequential,
            AgentNames = ["yazar", "editor"],
            RequirePlanApproval = true,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await AgentPrismTestHost.ReadJsonAsync(response);

        problem.GetProperty("detail").GetString()!.ShouldContain("requirePlanApproval", Case.Sensitive);
    }

    [Fact]
    public async Task Plan_onayi_tanimda_saklanir()
    {
        await using var host = await StartWithEngineAsync();

        using var response = await SaveAsync(host, "magentic", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Magentic,
            AgentNames = ["editor"],
            ManagerAgentName = "yazar",
            RequirePlanApproval = true,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        body.GetProperty("requirePlanApproval").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Beklemeyen_calistirmanin_bekleyen_istegi_yoktur()
    {
        await using var host = await StartWithEngineAsync();

        using (var saved = await SaveAsync(host, "zincir", Sequential()))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var runId = await RunAsync(host, "zincir");

        using var response = await host.Client.GetAsync(
            $"/agentprism/api/workflows/runs/{runId}/requests");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        body.EnumerateArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Olmayan_calistirmanin_istekleri_404_doner()
    {
        await using var host = await StartWithEngineAsync();

        using var response = await host.Client.GetAsync(
            $"/agentprism/api/workflows/runs/{AgentPrismId.NewId()}/requests");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Yanit_istek_kimligi_olmadan_reddedilir()
    {
        await using var host = await StartWithEngineAsync();

        using var response = await host.Client.PostAsJsonAsync(
            $"/agentprism/api/workflows/runs/{AgentPrismId.NewId()}/respond",
            new WorkflowRespondHttpRequest { RequestId = "  " });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Beklemeyen_calistirmaya_verilen_yanit_akista_hata_bildirir()
    {
        await using var host = await StartWithEngineAsync();

        using (var saved = await SaveAsync(host, "zincir", Sequential()))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var runId = await RunAsync(host, "zincir");

        using var response = await host.Client.PostAsJsonAsync(
            $"/agentprism/api/workflows/runs/{runId}/respond",
            new WorkflowRespondHttpRequest { RequestId = "herhangi" });

        // Akis basladiktan sonra durum kodu degistirilemez; hata bir SSE
        // cercevesi olarak gonderilir. Ayni desen calistirma ucunda da kullanildi.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        frames.ShouldContain(static frame => string.Equals(frame.Event, "error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Motor_kayitli_degilse_yanit_501_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            $"/agentprism/api/workflows/runs/{AgentPrismId.NewId()}/respond",
            new WorkflowRespondHttpRequest { RequestId = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    /// <summary>Bir workflow'u calistirir ve calistirma kimligini dondurur.</summary>
    private static async Task<Guid> RunAsync(AgentPrismTestHost host, string name)
    {
        using var response = await host.Client.PostAsJsonAsync(
            $"/agentprism/api/workflows/{name}/run",
            new WorkflowRunHttpRequest { Message = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        return JsonDocument.Parse(frames[0].Data).RootElement.GetProperty("runId").GetGuid();
    }

    private static Task<AgentPrismTestHost> StartWithEngineAsync()
        => AgentPrismTestHost.StartAsync(static builder => builder
            .AddAgent(TestData.Definition("yazar"))
            .AddAgent(TestData.Definition("editor"))
            .UseWorkflows());

    private static Task<HttpResponseMessage> SaveAsync(
        AgentPrismTestHost host,
        string name,
        WorkflowSaveRequest request)
        => host.Client.PutAsJsonAsync($"/agentprism/api/workflows/{name}", request);

    private static WorkflowSaveRequest Sequential()
        => new()
        {
            Description = "Iki adimli zincir.",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["yazar", "editor"],
        };
}
