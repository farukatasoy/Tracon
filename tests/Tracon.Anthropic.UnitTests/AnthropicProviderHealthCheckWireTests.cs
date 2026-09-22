using Tracon.Anthropic.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.Anthropic.UnitTests;

/// <summary>
/// What the Anthropic health check actually puts on the wire (phase 181). The
/// shared body calls a per-provider <c>authorize</c> delegate; this proves the
/// Anthropic shell hands it the Anthropic headers and no other provider's.
/// </summary>
public sealed class AnthropicProviderHealthCheckWireTests
{
    [Fact]
    public async Task Sends_x_api_key_and_the_required_version_header()
    {
        await using var server = StubHttpServer.Respond(200, "OK", """{"data":[{"id":"claude-test"}]}""");

        var health = await new AnthropicProviderHealthCheck(
                AnthropicProviderNames.Anthropic,
                TestData.Options(o => o.Endpoint = new Uri(server.BaseAddress, "v1")))
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Healthy);
        health.Models.ShouldBe(["claude-test"]);

        var head = await server.RequestHead;
        head.ShouldStartWith("GET /v1/models HTTP/1.1");
        head.ShouldContain($"x-api-key: {TestData.ApiKey}");
        head.ShouldContain($"anthropic-version: {AnthropicProviderHealthCheck.AnthropicVersion}");
        head.ShouldNotContain("Authorization:", Case.Insensitive);
    }
}
