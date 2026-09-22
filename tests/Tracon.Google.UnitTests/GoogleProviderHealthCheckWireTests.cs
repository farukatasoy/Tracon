using Tracon.Google.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.Google.UnitTests;

/// <summary>
/// What the Gemini health check actually puts on the wire (phase 181). The
/// shared body calls a per-provider <c>authorize</c> delegate; this proves the
/// Google shell hands it the Google header and keeps the key out of the URL.
/// </summary>
public sealed class GoogleProviderHealthCheckWireTests
{
    [Fact]
    public async Task Sends_x_goog_api_key_and_never_puts_the_key_in_the_query()
    {
        await using var server = StubHttpServer.Respond(200, "OK", """{"models":[{"name":"models/gemini-test"}]}""");

        var health = await new GoogleProviderHealthCheck(
                GoogleProviderNames.Google,
                TestData.Options(o => o.Endpoint = server.BaseAddress))
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Healthy);
        health.Models.ShouldBe(["gemini-test"]);

        var head = await server.RequestHead;
        head.ShouldStartWith($"GET /{GoogleProviderHealthCheck.DefaultApiVersion}/models HTTP/1.1");
        head.ShouldContain($"x-goog-api-key: {TestData.ApiKey}");
        head.Split("\r\n")[0].ShouldNotContain(TestData.ApiKey);
        head.ShouldNotContain("Authorization:", Case.Insensitive);
    }
}
