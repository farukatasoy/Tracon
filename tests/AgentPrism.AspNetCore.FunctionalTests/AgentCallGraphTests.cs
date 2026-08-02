using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Cagri grafiginin HTTP yuzeyi: kaydetme anindaki denetim ve calistirma agaci uclari.
/// </summary>
public sealed class AgentCallGraphTests
{
    private static readonly Uri Agents = new("/agentprism/api/agents", UriKind.Relative);

    [Fact]
    public async Task Kendini_cagiran_tanim_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Agents,
            TestData.Request() with { CallableAgentNames = ["db-agent"] });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await AgentPrismTestHost.ReadJsonAsync(response);
        Detail(problem).ShouldContain("kendisini cagiramaz", Case.Sensitive);
    }

    [Fact]
    public async Task Bilinmeyen_agent_adi_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Agents,
            TestData.Request() with { CallableAgentNames = ["hic-yok"] });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await AgentPrismTestHost.ReadJsonAsync(response);
        Detail(problem).ShouldContain("katalogda yok", Case.Sensitive);
    }

    [Fact]
    public async Task Dolayli_dongu_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        await CreateAsync(host, "a");
        await CreateAsync(host, "b");
        await UpdateAsync(host, "b", ["a"]);

        // b -> a zinciri hazir. a -> b eklenirse dongu kapanir.
        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/agents/a", UriKind.Relative),
            TestData.Request(name: "a") with { CallableAgentNames = ["b"] });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await AgentPrismTestHost.ReadJsonAsync(response);
        Detail(problem).ShouldContain("dongu", Case.Sensitive);
    }

    [Fact]
    public async Task Gecerli_grafik_kaydedilir_ve_katalogda_gorunur()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        await CreateAsync(host, "arastirmaci");
        await CreateAsync(host, "yonlendirici");
        await UpdateAsync(host, "yonlendirici", ["arastirmaci"]);

        using var list = await host.Client.GetAsync(Agents);
        var json = await AgentPrismTestHost.ReadJsonAsync(list);

        var router = json.EnumerateArray()
            .Single(static agent => string.Equals(agent.GetProperty("name").GetString(), "yonlendirici", StringComparison.Ordinal));

        router.GetProperty("callableAgentNames")
            .EnumerateArray()
            .Select(static name => name.GetString() ?? string.Empty)
            .ShouldBe(["arastirmaci"]);
    }

    [Fact]
    public async Task Runs_listesi_varsayilan_olarak_yalniz_kokleri_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var (rootId, childId) = await RecordTreeAsync(host);

        using var roots = await host.Client.GetAsync(new Uri("/agentprism/api/runs", UriKind.Relative));
        var rootIds = await IdsAsync(roots);

        rootIds.ShouldContain(id => string.Equals(id, rootId.ToString(), StringComparison.Ordinal));
        rootIds.ShouldNotContain(id => string.Equals(id, childId.ToString(), StringComparison.Ordinal));

        using var all = await host.Client.GetAsync(
            new Uri("/agentprism/api/runs?includeChildren=true", UriKind.Relative));

        var allIds = await IdsAsync(all);
        allIds.ShouldContain(id => string.Equals(id, rootId.ToString(), StringComparison.Ordinal));
        allIds.ShouldContain(id => string.Equals(id, childId.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Agac_ucu_alt_calistirmadan_da_tum_agaci_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var (rootId, childId) = await RecordTreeAsync(host);

        // Alt calistirmanin detayindan gelen istek de tum agaci dondurur:
        // kullanici kardes dallari gormeden agacin neresinde oldugunu anlayamaz.
        using var response = await host.Client.GetAsync(
            new Uri($"/agentprism/api/runs/{childId}/tree", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var ids = await IdsAsync(response);
        ids.ShouldBe([rootId.ToString(), childId.ToString()], ignoreOrder: true);
    }

    [Fact]
    public async Task Olmayan_calistirmanin_agaci_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri($"/agentprism/api/runs/{Guid.Empty}/tree", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Kok_kaydi_alt_calistirma_sayisini_ve_agac_toplamini_tasir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var (rootId, _) = await RecordTreeAsync(host);

        using var response = await host.Client.GetAsync(
            new Uri($"/agentprism/api/runs/{rootId}", UriKind.Relative));

        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        json.GetProperty("childRunCount").GetInt32().ShouldBe(1);
        json.GetProperty("depth").GetInt32().ShouldBe(0);

        // Agac toplami kokun kendi kullanimini da icerir; ikisi toplanmaz.
        json.GetProperty("usage").GetProperty("totalTokens").GetInt64().ShouldBe(10);
        json.GetProperty("treeUsage").GetProperty("totalTokens").GetInt64().ShouldBe(30);
    }

    /// <summary>Iki satirli bir calistirma agacini dogrudan depoya yazar.</summary>
    /// <remarks>
    /// Gercek bir model cagrisi yapilmaz: bu testlerin dogruladigi sey HTTP
    /// yuzeyidir, alt agent cagrisinin kendisi degil. Cagri yolu
    /// <c>ChildAgentInvokerTests</c> icinde test edilir.
    /// </remarks>
    private static async Task<(Guid RootId, Guid ChildId)> RecordTreeAsync(AgentPrismTestHost host)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var rootId = AgentPrismId.NewId();
        var childId = AgentPrismId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = rootId,
            AgentName = "yonlendirici",
            StartedAt = DateTimeOffset.UtcNow,
        });

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = childId,
            AgentName = "arastirmaci",
            StartedAt = DateTimeOffset.UtcNow,
            ParentRunId = rootId,
            RootRunId = rootId,
            Depth = 1,
        });

        await CompleteAsync(runs, rootId, 10);
        await CompleteAsync(runs, childId, 20);

        return (rootId, childId);
    }

    private static ValueTask CompleteAsync(IRunStore runs, Guid runId, long tokens)
        => runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage { InputTokens = tokens, OutputTokens = 0, TotalTokens = tokens },
        });

    private static async Task<List<string>> IdsAsync(HttpResponseMessage response)
    {
        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        return [.. json.EnumerateArray().Select(static run => run.GetProperty("id").GetString() ?? string.Empty)];
    }

    private static string Detail(System.Text.Json.JsonElement problem)
        => problem.GetProperty("detail").GetString() ?? string.Empty;

    private static async Task CreateAsync(AgentPrismTestHost host, string name)
    {
        using var response = await host.Client.PostAsJsonAsync(Agents, TestData.Request(name: name));

        response.EnsureSuccessStatusCode();
    }

    private static async Task UpdateAsync(AgentPrismTestHost host, string name, string[] callable)
    {
        using var response = await host.Client.PutAsJsonAsync(
            new Uri($"/agentprism/api/agents/{name}", UriKind.Relative),
            TestData.Request(name: name) with { CallableAgentNames = callable });

        response.EnsureSuccessStatusCode();
    }
}
