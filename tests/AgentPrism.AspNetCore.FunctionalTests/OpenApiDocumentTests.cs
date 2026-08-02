using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// AgentPrism uclarinin OpenAPI belgesinde gorundugunu dogrular.
/// </summary>
/// <remarks>
/// <c>AgentPrism.AspNetCore</c> bilerek <c>Microsoft.AspNetCore.OpenApi</c>
/// paketine <strong>bagimli degildir</strong>; bir kutuphane tuketicinin
/// bagimlilik grafigine OpenAPI uretimi dayatmamalidir. Bunun yerine uclar
/// paylasilan cerceveden gelen ustveriyi (<c>WithName</c>, <c>WithTags</c>,
/// <c>WithSummary</c>, <c>WithDescription</c>) tasir; tuketici kendi uygulamasinda
/// <c>AddOpenApi()</c> cagirdiginda belge kendiliginden olusur. Bu test tam olarak
/// o senaryoyu kurar.
/// </remarks>
public sealed class OpenApiDocumentTests
{
    [Fact]
    public async Task Belge_uretilir_ve_AgentPrism_uclarini_icerir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(withOpenApi: true);

        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        response.EnsureSuccessStatusCode();

        var paths = (await AgentPrismTestHost.ReadJsonAsync(response)).GetProperty("paths");

        paths.TryGetProperty("/agentprism/api/meta", out _).ShouldBeTrue();
        paths.TryGetProperty("/agentprism/api/agents", out _).ShouldBeTrue();
        paths.TryGetProperty("/agentprism/api/agents/{name}", out _).ShouldBeTrue();
        paths.TryGetProperty("/agentprism/api/runs/{runId}/events", out _).ShouldBeTrue();
        paths.TryGetProperty("/agentprism/v1/responses", out _).ShouldBeTrue();
        paths.TryGetProperty("/agentprism/v1/chat/completions", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Belge_ozet_ve_etiket_ustverisini_tasir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(withOpenApi: true);

        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        var meta = (await AgentPrismTestHost.ReadJsonAsync(response))
            .GetProperty("paths")
            .GetProperty("/agentprism/api/meta")
            .GetProperty("get");

        meta.GetProperty("summary").GetString().ShouldNotBeNullOrWhiteSpace();
        meta.GetProperty("tags")[0].GetString().ShouldBe("AgentPrism");
        meta.GetProperty("operationId").GetString().ShouldBe("AgentPrismMeta");
    }

    [Fact]
    public async Task Ozel_prefix_belgede_de_gecerlidir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(prefix: "/yonetim", withOpenApi: true);

        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        (await AgentPrismTestHost.ReadJsonAsync(response))
            .GetProperty("paths")
            .TryGetProperty("/yonetim/api/meta", out _)
            .ShouldBeTrue();
    }
}
