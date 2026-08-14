using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>A/B deneyi uclarinin bellek ici davranis testleri (Faz 19.3-19.4).</summary>
public sealed class ExperimentEndpointTests
{
    private static readonly Uri Experiments = new("/agentprism/api/experiments", UriKind.Relative);
    private static readonly Uri Experiment = new("/agentprism/api/experiments/surum-karsilastirma", UriKind.Relative);
    private static readonly Uri Start = new("/agentprism/api/experiments/surum-karsilastirma/start", UriKind.Relative);
    private static readonly Uri Stop = new("/agentprism/api/experiments/surum-karsilastirma/stop", UriKind.Relative);
    private static readonly Uri Results = new("/agentprism/api/experiments/surum-karsilastirma/results", UriKind.Relative);
    private static readonly Uri Canary = new("/agentprism/api/experiments/surum-karsilastirma/canary", UriKind.Relative);

    [Fact]
    public async Task Deney_olusturulur_ve_listelenir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);

        using var created = await host.Client.PutAsJsonAsync(Experiment, Request());
        created.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(created);
        body.GetProperty("status").GetString().ShouldBe("Draft");
        body.GetProperty("variants").GetArrayLength().ShouldBe(2);

        using var listed = await host.Client.GetAsync(Experiments);
        (await AgentPrismTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Agirlik_toplami_100_degilse_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);

        using var response = await host.Client.PutAsJsonAsync(
            Experiment,
            Request() with
            {
                Variants =
                [
                    new ExperimentVariant { Name = "control", Version = 1, Weight = 40 },
                    new ExperimentVariant { Name = "v2", Version = 2, Weight = 40 },
                ],
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Kod_kaynakli_agentta_deney_kurulamaz()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition(name: "kod-agenti")));

        using var response = await host.Client.PutAsJsonAsync(
            Experiment,
            Request() with { AgentName = "kod-agenti" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("version history");
    }

    [Fact]
    public async Task Olmayan_surumlu_varyant_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);

        using var response = await host.Client.PutAsJsonAsync(
            Experiment,
            Request() with
            {
                Variants =
                [
                    new ExperimentVariant { Name = "control", Version = 1, Weight = 50 },
                    new ExperimentVariant { Name = "v2", Version = 99, Weight = 50 },
                ],
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Yasam_dongusu_baslar_ve_durur()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Experiment, Request());

        using var started = await host.Client.PostAsJsonAsync(Start, new { });
        started.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AgentPrismTestHost.ReadJsonAsync(started)).GetProperty("status").GetString().ShouldBe("Running");

        using var stopped = await host.Client.PostAsJsonAsync(Stop, new { });
        stopped.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AgentPrismTestHost.ReadJsonAsync(stopped)).GetProperty("status").GetString().ShouldBe("Stopped");
    }

    [Fact]
    public async Task Ayni_agent_icin_ikinci_calisan_deney_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);

        await host.Client.PutAsJsonAsync(Experiment, Request());
        await host.Client.PostAsJsonAsync(Start, new { });

        var secondUri = new Uri("/agentprism/api/experiments/ikinci-deney", UriKind.Relative);
        using (var created = await host.Client.PutAsJsonAsync(secondUri, Request()))
        {
            created.EnsureSuccessStatusCode();
        }

        using var secondStart = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/experiments/ikinci-deney/start", UriKind.Relative), new { });

        secondStart.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Calisirken_duzenlenemez()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Experiment, Request());
        await host.Client.PostAsJsonAsync(Start, new { });

        using var response = await host.Client.PutAsJsonAsync(Experiment, Request());

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Sonuc_ucu_bos_deneyde_bos_liste_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Experiment, Request());

        using var response = await host.Client.GetAsync(Results);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("results").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Kanarya_kurali_tanimlanir_ve_okunur()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Experiment, Request());

        using var set = await host.Client.PutAsJsonAsync(Canary, CanaryPolicy());
        set.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AgentPrismTestHost.ReadJsonAsync(set)).GetProperty("canary").GetProperty("canaryVariant").GetString().ShouldBe("v2");

        using var fetched = await host.Client.GetAsync(Canary);
        fetched.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(fetched);
        body.GetProperty("policy").GetProperty("canaryVariant").GetString().ShouldBe("v2");

        // Hic calistirma yok -- degerlendirme "yetersiz veri" doner, geri alma DEGIL.
        body.GetProperty("evaluation").GetProperty("decision").GetString().ShouldBe("InsufficientData");
    }

    [Fact]
    public async Task Kanarya_kurali_null_govdeyle_kaldirilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Experiment, Request());
        await host.Client.PutAsJsonAsync(Canary, CanaryPolicy());

        using var content = new StringContent("null", System.Text.Encoding.UTF8, "application/json");
        using var cleared = await host.Client.PutAsync(Canary, content);
        cleared.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AgentPrismTestHost.ReadJsonAsync(cleared)).GetProperty("canary").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);

        using var fetched = await host.Client.GetAsync(Canary);
        var body = await AgentPrismTestHost.ReadJsonAsync(fetched);
        body.GetProperty("policy").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        body.GetProperty("evaluation").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Ikiden_farkli_kollu_deneyde_kanarya_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);

        using var created = await host.Client.PutAsJsonAsync(
            Experiment,
            Request() with
            {
                Variants =
                [
                    new ExperimentVariant { Name = "control", Version = 1, Weight = 34 },
                    new ExperimentVariant { Name = "v2", Version = 2, Weight = 33 },
                    new ExperimentVariant { Name = "v3", Version = 1, Weight = 33 },
                ],
            });
        created.EnsureSuccessStatusCode();

        using var response = await host.Client.PutAsJsonAsync(Canary, CanaryPolicy());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("two-variant");
    }

    [Fact]
    public async Task Olmayan_kolla_kanarya_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Experiment, Request());

        using var response = await host.Client.PutAsJsonAsync(Canary, CanaryPolicy() with { CanaryVariant = "yok" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Olmayan_deneyde_kanarya_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(Canary, CanaryPolicy());

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static CanaryPolicy CanaryPolicy()
        => new()
        {
            CanaryVariant = "v2",
            MaxErrorRateDelta = 0.1,
            MinSampleSize = 20,
        };

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

    private static ExperimentSaveRequest Request()
        => new()
        {
            AgentName = "musteri-destek-agent",
            Variants =
            [
                new ExperimentVariant { Name = "control", Version = 1, Weight = 50 },
                new ExperimentVariant { Name = "v2", Version = 2, Weight = 50 },
            ],
        };
}
