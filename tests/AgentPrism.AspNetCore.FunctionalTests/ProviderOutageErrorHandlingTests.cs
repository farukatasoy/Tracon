using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Aile H (HATA-S2-003/HATA-S3-005) regresyon testleri: akissiz calistirma
/// uclarinin dar istisna filtresi gercek saglayici SDK istisnalarini kacirip
/// ASP.NET Core'un genel isleyicisine sizdiriyordu — istemci beklenen
/// <c>502</c>/<c>upstream_error</c> yerine ciplak <c>500</c> aliyordu. K-296/K-384
/// bu kalibi akisli (SSE) kardes yollarda zaten duzeltmisti; burada duzeltilen
/// UC akissiz yol da AYNI <see cref="ThrowingModelProvider"/> ile dogrulanir.
/// </summary>
public sealed class ProviderOutageErrorHandlingTests
{
    private const string AgentName = "kirik-agent";
    private const string IdempotencyHeader = "Idempotency-Key";

    private static void ConfigureBrokenAgent(IAgentPrismBuilder builder)
    {
        builder
            .AddModelProvider(new ThrowingModelProvider())
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Kisa yanit ver.",
                Model = new ModelBinding { Provider = "kirik", Model = "kirik-1" },
            });
    }

    [Fact]
    public async Task Akissiz_calistirma_ucu_saglayici_hatasinda_502_ProblemDetails_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(ConfigureBrokenAgent);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/agentprism/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "merhaba" }),
        };
        request.Headers.Add(IdempotencyHeader, Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var json = await AgentPrismTestHost.ReadJsonAsync(response);
        json.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Responses_ucu_saglayici_hatasinda_502_upstream_error_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(ConfigureBrokenAgent);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/v1/responses", UriKind.Relative),
            new { model = AgentName, input = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var json = await AgentPrismTestHost.ReadJsonAsync(response);
        json.GetProperty("error").GetProperty("type").GetString().ShouldBe("upstream_error");
    }

    [Fact]
    public async Task ChatCompletions_ucu_saglayici_hatasinda_502_upstream_error_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(ConfigureBrokenAgent);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/v1/chat/completions", UriKind.Relative),
            new
            {
                model = AgentName,
                messages = new[] { new { role = "user", content = "merhaba" } },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var json = await AgentPrismTestHost.ReadJsonAsync(response);
        json.GetProperty("error").GetProperty("type").GetString().ShouldBe("upstream_error");
    }
}
