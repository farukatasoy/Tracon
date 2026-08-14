using System.Net;
using System.Net.Http.Json;
using System.Text;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Govde baglamasi bir <c>JsonException</c>'a carpinca genel <c>500</c> yerine
/// bu kutuphanenin kendi <c>400</c> <c>ProblemDetails</c> sozlesmesine dustugunu
/// dogrular (HATA-S2-006, HATA-S2-007).
/// </summary>
/// <remarks>
/// Uc endpoint kasitli secildi: biri eskiden <c>[FromBody]</c> ile otomatik
/// baglama kullaniyordu (<c>ApiKeyEndpoints</c>), biri <c>[FromBody]</c>
/// OZNITELIGI OLMADAN ortuk baglama kullaniyordu (<c>GovernanceEndpoints</c> —
/// HATA-S2-007'nin grep tabanli "<c>[FromBody]</c> kullanan 10 dosya" tahmini
/// bu yuzden bu ucu kacirmisti), biri ise sayisal alan tipi uyusmazligidir
/// (enum disi bir JSON hatasi sinifi). Ucu de artik <c>RequestBodyBinding.ReadAsync</c>
/// ile govdeyi elle okur — bu ortamdan (Development/Production) BAGIMSIZ calisir;
/// <c>JsonBindingProblemMiddleware</c> yalniz elle okumayi unutan gelecekteki bir
/// uc icin (yalniz Development'ta) savunma katmanidir.
/// </remarks>
public sealed class JsonBindingProblemMiddlewareTests
{
    [Fact]
    public async Task Taninmayan_enum_degeri_api_anahtari_govdesinde_400_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/agentprism/api/api-keys",
            new { name = "gecersiz", scopes = new[] { "runs:hepsi" } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var json = await AgentPrismTestHost.ReadJsonAsync(response);
        json.GetProperty("title").GetString().ShouldBe("Invalid request body");
        (json.GetProperty("detail").GetString() ?? string.Empty).ShouldContain("ApiKeyScope");
    }

    [Fact]
    public async Task Taninmayan_enum_degeri_ortuk_baglamada_da_400_doner()
    {
        // GovernanceEndpoints.SaveAsync govdeyi [FromBody] OZNITELIGI OLMADAN
        // (ortuk baglama) alir; HATA-S2-006'nin "[FromBody] kullanan 10 dosya"
        // tahmini bu yuzden bu ucu kacirmisti (HATA-S2-007).
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/mcp-servers/stdio-denemesi", UriKind.Relative),
            new { endpoint = "stdio://bir-komut", transport = "Stdio" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var json = await AgentPrismTestHost.ReadJsonAsync(response);
        json.GetProperty("title").GetString().ShouldBe("Invalid request body");
        (json.GetProperty("detail").GetString() ?? string.Empty).ShouldContain("McpTransportMode");
    }

    [Fact]
    public async Task Sayisal_alana_metin_gonderilirse_saklama_ucu_da_400_doner()
    {
        // Kusur sinifi enum'a ozgu degildir: herhangi bir tur uyusmazligi ayni
        // JsonException -> BadHttpRequestException zincirini tetikler.
        await using var host = await AgentPrismTestHost.StartAsync();

        using var content = new StringContent(
            """{ "maxAgeDays": "not-a-number", "archive": true }""",
            Encoding.UTF8,
            "application/json");

        using var response = await host.Client.PutAsync(
            new Uri("/agentprism/api/retention/runs", UriKind.Relative),
            content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var json = await AgentPrismTestHost.ReadJsonAsync(response);
        json.GetProperty("title").GetString().ShouldBe("Invalid request body");
    }
}
