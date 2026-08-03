using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Agent tanimi yasam dongusu: olusturma, okuma, guncelleme, surumleme,
/// geri alma ve silme.
/// </summary>
public sealed class AgentCrudTests
{
    private static readonly Uri Agents = new("/agentprism/api/agents", UriKind.Relative);

    [Fact]
    public async Task Tanim_olusturulur_ve_katalogda_gorunur()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request());
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var list = await host.Client.GetAsync(Agents);
        var json = await AgentPrismTestHost.ReadJsonAsync(list);

        json.EnumerateArray()
            .Select(static agent => agent.GetProperty("name").GetString())
            .ShouldContain(static name => name == "db-agent");
    }

    [Fact]
    public async Task Guncelleme_yeni_surum_uretir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request()))
        {
            created.EnsureSuccessStatusCode();
        }

        using var updated = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/agents/db-agent", UriKind.Relative),
            TestData.Request(instructions: "Artik uzun yanit ver."));

        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AgentPrismTestHost.ReadJsonAsync(updated)).GetProperty("version").GetInt32().ShouldBe(2);

        using var versions = await host.Client.GetAsync(
            new Uri("/agentprism/api/agents/db-agent/versions", UriKind.Relative));

        (await AgentPrismTestHost.ReadJsonAsync(versions)).GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task Geri_alma_eski_icerigi_yeni_surum_olarak_yazar()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request(instructions: "ilk")))
        {
            created.EnsureSuccessStatusCode();
        }

        using (var updated = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/agents/db-agent", UriKind.Relative),
            TestData.Request(instructions: "ikinci")))
        {
            updated.EnsureSuccessStatusCode();
        }

        using var rolledBack = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/db-agent/rollback", UriKind.Relative),
            new AgentRollbackRequest { Version = 1 });

        rolledBack.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await AgentPrismTestHost.ReadJsonAsync(rolledBack);
        json.GetProperty("instructions").GetString().ShouldBe("ilk");

        // Geri alma eski surumu SILMEZ; icerigini yeni surum olarak yazar.
        json.GetProperty("version").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task Silme_tanimi_kaldirir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request()))
        {
            created.EnsureSuccessStatusCode();
        }

        using var deleted = await host.Client.DeleteAsync(
            new Uri("/agentprism/api/agents/db-agent", UriKind.Relative));

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var missing = await host.Client.GetAsync(
            new Uri("/agentprism/api/agents/db-agent", UriKind.Relative));

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Detay_kod_agentini_duzenlenemez_isaretler()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/api/agents/kod-agent", UriKind.Relative));

        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        json.GetProperty("isEditable").GetBoolean().ShouldBeFalse();
        json.GetProperty("descriptor").GetProperty("origin").GetString().ShouldBe("Code");
        json.TryGetProperty("definition", out var definition).ShouldBeTrue();
        definition.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Detay_veritabani_agentini_duzenlenebilir_isaretler()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request()))
        {
            created.EnsureSuccessStatusCode();
        }

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/api/agents/db-agent", UriKind.Relative));

        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        json.GetProperty("isEditable").GetBoolean().ShouldBeTrue();
        json.GetProperty("definition").GetProperty("name").GetString().ShouldBe("db-agent");
    }

    // --- Kod agent'i korumasi: ad cakismasinda kod kazanir (K-003) ---

    [Fact]
    public async Task Kod_agentiyle_ayni_ada_tanim_yazilamaz()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(Agents, TestData.Request(name: "kod-agent"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("kodda tanimli");
    }

    [Fact]
    public async Task Kod_agenti_guncellenemez()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/agents/kod-agent", UriKind.Relative),
            TestData.Request(name: "kod-agent"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Kod_agenti_silinemez()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.DeleteAsync(
            new Uri("/agentprism/api/agents/kod-agent", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // --- Dogrulama ---

    [Fact]
    public async Task Yoldaki_ad_ile_govdedeki_ad_uyusmalidir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/agents/bir-ad", UriKind.Relative),
            TestData.Request(name: "baska-ad"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Olmayan_tanim_guncellenemez()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/agents/yok-boyle", UriKind.Relative),
            TestData.Request(name: "yok-boyle"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Ayni_ad_ikinci_kez_olusturulamaz()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var first = await host.Client.PostAsJsonAsync(Agents, TestData.Request()))
        {
            first.EnsureSuccessStatusCode();
        }

        using var second = await host.Client.PostAsJsonAsync(Agents, TestData.Request());

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Sikistirma_ve_bellek_ayarlari_gidip_gelir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var request = TestData.Request() with
        {
            Compaction = new CompactionSettings
            {
                Strategy = CompactionStrategyKind.SlidingWindow,
                TriggerMessages = 40,
                MinimumPreservedTurns = 3,
            },
            Memory = new MemorySettings { EnableTodo = true, EnableTextSearch = true },
        };

        using (var created = await host.Client.PostAsJsonAsync(Agents, request))
        {
            created.EnsureSuccessStatusCode();
        }

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/api/agents/db-agent", UriKind.Relative));

        var json = await AgentPrismTestHost.ReadJsonAsync(response);
        var definition = json.GetProperty("definition");

        definition.GetProperty("compaction").GetProperty("strategy").GetString().ShouldBe("SlidingWindow");
        definition.GetProperty("compaction").GetProperty("triggerMessages").GetInt32().ShouldBe(40);
        definition.GetProperty("memory").GetProperty("enableTodo").GetBoolean().ShouldBeTrue();
        definition.GetProperty("memory").GetProperty("enableTextSearch").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Olmayan_surume_geri_alinamaz()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request()))
        {
            created.EnsureSuccessStatusCode();
        }

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/db-agent/rollback", UriKind.Relative),
            new AgentRollbackRequest { Version = 99 });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // --- Surum diff'i (Faz 19.1) ---

    [Fact]
    public async Task Surum_diffi_iki_ham_tanimi_dondurur()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request(instructions: "ilk")))
        {
            created.EnsureSuccessStatusCode();
        }

        using (var updated = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/agents/db-agent", UriKind.Relative),
            TestData.Request(instructions: "ikinci")))
        {
            updated.EnsureSuccessStatusCode();
        }

        using var diff = await host.Client.GetAsync(
            new Uri("/agentprism/api/agents/db-agent/versions/1/diff/2", UriKind.Relative));

        diff.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await AgentPrismTestHost.ReadJsonAsync(diff);
        json.GetProperty("left").GetProperty("instructions").GetString().ShouldBe("ilk");
        json.GetProperty("right").GetProperty("instructions").GetString().ShouldBe("ikinci");
    }

    [Fact]
    public async Task Olmayan_surumun_diffi_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request()))
        {
            created.EnsureSuccessStatusCode();
        }

        using var diff = await host.Client.GetAsync(
            new Uri("/agentprism/api/agents/db-agent/versions/1/diff/99", UriKind.Relative));

        diff.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
