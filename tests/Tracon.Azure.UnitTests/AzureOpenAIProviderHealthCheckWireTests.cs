using Tracon.Azure.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.Azure.UnitTests;

/// <summary>
/// What the Azure OpenAI health check actually puts on the wire (phase 181).
/// The shared body calls a per-provider <c>authorize</c> delegate; this proves
/// the Azure shell hands it the <c>api-key</c> header, or a Bearer token when a
/// credential factory is set.
/// </summary>
public sealed class AzureOpenAIProviderHealthCheckWireTests
{
    [Fact]
    public async Task Sends_the_key_in_the_api_key_header()
    {
        await using var server = StubHttpServer.Respond(200, "OK", """{"data":[{"id":"gpt-test"}]}""");

        var health = await new AzureOpenAIProviderHealthCheck(
                AzureOpenAIProviderNames.AzureOpenAI,
                TestData.Options(o => o.Endpoint = server.BaseAddress))
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Healthy);

        var head = await server.RequestHead;
        head.ShouldStartWith($"GET /openai/models?api-version={AzureOpenAIProviderHealthCheck.ApiVersion} HTTP/1.1");
        head.ShouldContain($"api-key: {TestData.ApiKey}");
        head.ShouldNotContain("Authorization:", Case.Insensitive);
    }

    [Fact]
    public async Task Credential_factory_wins_over_the_key_and_sends_a_Bearer_token()
    {
        await using var server = StubHttpServer.Respond(200, "OK", """{"data":[]}""");

        await new AzureOpenAIProviderHealthCheck(
                AzureOpenAIProviderNames.AzureOpenAI,
                TestData.Options(o =>
                {
                    o.Endpoint = server.BaseAddress;
                    o.CredentialFactory = static () => new FakeTokenCredential("entra-token");
                }))
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        var head = await server.RequestHead;
        head.ShouldContain("Authorization: Bearer entra-token");
        head.ShouldNotContain("api-key:", Case.Insensitive);
    }
}
