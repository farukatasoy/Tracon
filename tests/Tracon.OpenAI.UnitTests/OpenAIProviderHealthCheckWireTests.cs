using Tracon.OpenAI.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// What the OpenAI health check actually puts on the wire (phase 181). The
/// shared body calls a per-provider <c>authorize</c> delegate; this proves the
/// OpenAI shell hands it the OpenAI header and no other provider's.
/// </summary>
public sealed class OpenAIProviderHealthCheckWireTests
{
    [Fact]
    public async Task Sends_the_key_as_a_Bearer_token_to_the_models_path()
    {
        await using var server = StubHttpServer.Respond(200, "OK", """{"data":[{"id":"gpt-test"}]}""");

        var health = await new OpenAIProviderHealthCheck(
                OpenAIProviderNames.ChatCompletions,
                TestData.Options(o => o.Endpoint = new Uri(server.BaseAddress, "v1")))
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Healthy);
        health.Models.ShouldBe(["gpt-test"]);

        var head = await server.RequestHead;
        head.ShouldStartWith("GET /v1/models HTTP/1.1");
        head.ShouldContain($"Authorization: Bearer {TestData.ApiKey}");
        head.ShouldNotContain("x-api-key", Case.Insensitive);
        head.ShouldNotContain("api-key:", Case.Insensitive);
    }
}
