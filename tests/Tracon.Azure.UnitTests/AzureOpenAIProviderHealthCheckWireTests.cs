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
    public async Task Sends_the_key_as_a_Bearer_token_like_the_chat_call_does()
    {
        // One key, one header form: the chat call sends `Authorization: Bearer`,
        // so a gateway that accepts only one form cannot report Healthy here and
        // reject the first real call.
        await using var server = StubHttpServer.Respond(200, "OK", """{"data":[{"id":"gpt-test"}]}""");

        var health = await new AzureOpenAIProviderHealthCheck(
                AzureOpenAIProviderNames.AzureOpenAI,
                TestData.Options(o => o.Endpoint = server.BaseAddress))
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Healthy);

        var head = await server.RequestHead;
        head.ShouldStartWith("GET /openai/v1/models HTTP/1.1");
        head.ShouldContain($"Authorization: Bearer {TestData.ApiKey}");
        head.ShouldNotContain("api-key:", Case.Insensitive);
    }

    [Fact]
    public async Task An_address_that_already_ends_in_openai_v1_is_asked_once()
    {
        await using var server = StubHttpServer.Respond(200, "OK", """{"data":[]}""");

        await new AzureOpenAIProviderHealthCheck(
                AzureOpenAIProviderNames.AzureOpenAI,
                TestData.Options(o => o.Endpoint = new Uri(server.BaseAddress, "openai/v1")))
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        (await server.RequestHead).ShouldStartWith("GET /openai/v1/models HTTP/1.1");
    }

    [Fact]
    public async Task An_Entra_credential_is_never_sent_over_plain_HTTP()
    {
        // The chat call refuses a non-HTTPS address for an Entra token; the
        // periodic health check must not be the way around that.
        await using var server = StubHttpServer.Respond(200, "OK", """{"data":[]}""");
        var credential = new FakeTokenCredential("entra-token");

        var health = await new AzureOpenAIProviderHealthCheck(
                AzureOpenAIProviderNames.AzureOpenAI,
                TestData.Options(o =>
                {
                    o.Endpoint = server.BaseAddress;
                    o.ApiKey = null;
                    o.CredentialFactory = () => credential;
                }))
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Unhealthy);
        health.Detail.ShouldNotBeNull().ShouldContain("HTTPS");
        credential.RequestedTokenCount.ShouldBe(0, "no token is even requested");
        server.RequestHead.IsCompleted.ShouldBeFalse("nothing reached the server");
    }

    [Theory]
    [InlineData(null, AzureOpenAIProviderHealthCheck.DefaultAudience)]
    [InlineData("https://cognitiveservices.azure.us/.default", "https://cognitiveservices.azure.us/.default")]
    public async Task An_Entra_credential_over_HTTPS_sends_a_Bearer_token_for_the_requested_scope(string? audience, string expectedScope)
    {
        var credential = new FakeTokenCredential("entra-token");
        var check = new AzureOpenAIProviderHealthCheck(
            AzureOpenAIProviderNames.AzureOpenAI,
            TestData.Options(o =>
            {
                o.ApiKey = null;
                o.Audience = audience;
                o.CredentialFactory = () => credential;
            }));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://test-resource.openai.azure.com/openai/v1/models");

        await check.AuthorizeAsync(request, TestContext.Current.CancellationToken);

        request.Headers.Authorization.ShouldNotBeNull().ToString().ShouldBe("Bearer entra-token");
        credential.LastScope.ShouldBe(expectedScope);
    }
}
