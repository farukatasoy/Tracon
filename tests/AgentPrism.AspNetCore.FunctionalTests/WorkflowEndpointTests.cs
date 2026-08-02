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

        problem.GetProperty("detail").GetString()!.ShouldContain("hicbir agent icermiyor", Case.Sensitive);
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
