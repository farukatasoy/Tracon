using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>Eval takimi/vaka/kosu uclarinin bellek ici davranis testleri (Faz 18).</summary>
public sealed class EvalEndpointTests
{
    private static readonly Uri Suites = new("/agentprism/api/evals", UriKind.Relative);
    private static readonly Uri Suite = new("/agentprism/api/evals/musteri-destek-takimi", UriKind.Relative);
    private static readonly Uri Cases = new("/agentprism/api/evals/musteri-destek-takimi/cases", UriKind.Relative);
    private static readonly Uri Run = new("/agentprism/api/evals/musteri-destek-takimi/run", UriKind.Relative);
    private static readonly Uri Runs = new("/agentprism/api/evals/musteri-destek-takimi/runs", UriKind.Relative);

    [Fact]
    public async Task Takim_olusturulur_guncellenir_ve_silinir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PutAsJsonAsync(Suite, Request()))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await AgentPrismTestHost.ReadJsonAsync(created);
            body.GetProperty("agentName").GetString().ShouldBe("musteri-destek-agent");
        }

        using (var updated = await host.Client.PutAsJsonAsync(Suite, Request() with { Description = "guncellendi" }))
        {
            updated.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await AgentPrismTestHost.ReadJsonAsync(updated)).GetProperty("description").GetString()
                .ShouldBe("guncellendi");
        }

        using (var listed = await host.Client.GetAsync(Suites))
        {
            (await AgentPrismTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(1);
        }

        using var deleted = await host.Client.DeleteAsync(Suite);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var getAfterDelete = await host.Client.GetAsync(Suite);
        getAfterDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Bilinmeyen_denetim_turu_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            Suite,
            Request() with { Checks = JsonDocument.Parse("""[{"kind":"boyleBirSeyYok"}]""").RootElement });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Agent_adi_bos_ise_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(Suite, Request() with { AgentName = " " });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Var_olmayan_takim_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/evals/yok", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Vakalar_degistirilir_ve_temizlenir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        await host.Client.PutAsJsonAsync(Suite, Request());

        using (var saved = await host.Client.PutAsJsonAsync(
                   Cases,
                   new object[]
                   {
                       new { query = "birinci soru", expectedOutput = "beklenen" },
                       new { query = "ikinci soru", expectedTools = new[] { "get_order_status" } },
                   }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await AgentPrismTestHost.ReadJsonAsync(saved);
            body.GetArrayLength().ShouldBe(2);
            body[0].GetProperty("seq").GetInt32().ShouldBe(0);
            body[1].GetProperty("expectedTools")[0].GetString().ShouldBe("get_order_status");
        }

        using (var listed = await host.Client.GetAsync(Cases))
        {
            (await AgentPrismTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(2);
        }

        using var cleared = await host.Client.DeleteAsync(Cases);
        cleared.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var listedAfterClear = await host.Client.GetAsync(Cases);
        (await AgentPrismTestHost.ReadJsonAsync(listedAfterClear)).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Vakasiz_takim_calistirilamaz()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        await host.Client.PutAsJsonAsync(Suite, Request());

        using var response = await host.Client.PostAsJsonAsync(Run, new { });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Kosu_tetiklenir_is_uretir_ve_listelenebilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        await host.Client.PutAsJsonAsync(Suite, Request());
        await host.Client.PutAsJsonAsync(Cases, new object[] { new { query = "soru" } });

        using var triggered = await host.Client.PostAsJsonAsync(Run, new { });

        triggered.StatusCode.ShouldBe(HttpStatusCode.OK);
        var run = await AgentPrismTestHost.ReadJsonAsync(triggered);
        run.GetProperty("status").GetString().ShouldBe("Pending");
        run.GetProperty("total").GetInt32().ShouldBe(1);
        var runId = run.GetProperty("id").GetGuid();
        var jobId = run.GetProperty("jobId").GetGuid();

        using var job = await host.Client.GetAsync(new Uri($"/agentprism/api/jobs/{jobId}", UriKind.Relative));
        job.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AgentPrismTestHost.ReadJsonAsync(job)).GetProperty("job").GetProperty("kind").GetString()
            .ShouldBe("Eval");

        using var listed = await host.Client.GetAsync(Runs);
        (await AgentPrismTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(1);

        using var detail = await host.Client.GetAsync(new Uri($"/agentprism/api/evals/runs/{runId}", UriKind.Relative));
        detail.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await AgentPrismTestHost.ReadJsonAsync(detail);
        body.GetProperty("run").GetProperty("id").GetGuid().ShouldBe(runId);
        body.GetProperty("results").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Var_olmayan_kosu_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri($"/agentprism/api/evals/runs/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Operator_tetikleyebilir_ama_takim_kaydedemez()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Operator, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(AgentPrismPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        // Takim HTTP yetkilendirmesini atlayarak dogrudan depoya yazilir: bu testte
        // Admin ucu kapali, o yuzden takim baska bir yoldan tohumlanir.
        var evalStore = host.Services.GetRequiredService<IEvalStore>();
        var suite = await evalStore.SaveSuiteAsync(new EvalSuite
        {
            TenantId = "default",
            Name = "musteri-destek-takimi",
            AgentName = "musteri-destek-agent",
            Checks = JsonDocument.Parse("""[{"kind":"nonEmpty"}]""").RootElement,
        });
        await evalStore.ReplaceCasesAsync(suite.Id, [new EvalCase { SuiteId = suite.Id, Seq = 0, Query = "soru" }]);

        using var trigger = await host.Client.PostAsJsonAsync(Run, new { });
        trigger.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var save = await host.Client.PutAsJsonAsync(Suite, Request());
        save.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Surum secimi (Faz 19, acik soru 2) ---

    [Fact]
    public async Task Belirli_surumle_kosu_tetiklenir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Suite, Request());
        await host.Client.PutAsJsonAsync(Cases, new object[] { new { query = "soru" } });

        using var triggered = await host.Client.PostAsJsonAsync(Run, new { agentVersion = 1 });

        triggered.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Olmayan_surumle_kosu_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Suite, Request());
        await host.Client.PutAsJsonAsync(Cases, new object[] { new { query = "soru" } });

        using var triggered = await host.Client.PostAsJsonAsync(Run, new { agentVersion = 99 });

        triggered.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Kod_kaynakli_agentta_surum_secilemez()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition(name: "musteri-destek-agent")),
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        await host.Client.PutAsJsonAsync(Suite, Request());
        await host.Client.PutAsJsonAsync(Cases, new object[] { new { query = "soru" } });

        using var triggered = await host.Client.PostAsJsonAsync(Run, new { agentVersion = 1 });

        triggered.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await triggered.Content.ReadAsStringAsync()).ShouldContain("surum gecmisi");
    }

    /// <summary>"musteri-destek-agent" adinda, iki surumu olan bir veritabani agent'i olusturur.</summary>
    private static async Task CreateVersionedAgentAsync(AgentPrismTestHost host)
    {
        using var created = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents", UriKind.Relative),
            TestData.Request(name: "musteri-destek-agent", instructions: "ilk"));
        created.EnsureSuccessStatusCode();

        using var updated = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/agents/musteri-destek-agent", UriKind.Relative),
            TestData.Request(name: "musteri-destek-agent", instructions: "ikinci"));
        updated.EnsureSuccessStatusCode();
    }

    private static EvalSuiteSaveRequest Request()
        => new()
        {
            AgentName = "musteri-destek-agent",
            Checks = JsonDocument.Parse("""[{"kind":"nonEmpty","minLength":1}]""").RootElement,
        };
}
