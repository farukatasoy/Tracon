using System.Net;
using System.Text;
using AgentPrism.Azure.UnitTests.Infrastructure;

namespace AgentPrism.Azure.UnitTests;

/// <summary>
/// The health check's address joining, response parsing, and credential selection.
/// No call is made to a real Azure resource.
/// </summary>
public sealed class AzureOpenAIProviderHealthCheckTests
{
    [Fact]
    public void Base_address_with_trailing_slash_joins_to_the_data_plane_path()
        => AzureOpenAIProviderHealthCheck.BuildModelsEndpoint(TestData.Endpoint)
            .ToString().ShouldBe(
                $"{TestData.EndpointText}openai/models?api-version={AzureOpenAIProviderHealthCheck.ApiVersion}");

    [Fact]
    public void Base_address_without_trailing_slash_does_not_swallow_the_last_segment()
        => AzureOpenAIProviderHealthCheck.BuildModelsEndpoint(new Uri("https://example.gateway/azure"))
            .ToString().ShouldBe(
                $"https://example.gateway/azure/openai/models?api-version={AzureOpenAIProviderHealthCheck.ApiVersion}");

    [Fact]
    public async Task Model_ids_are_read_from_the_response_and_sorted()
    {
        using var response = Json("""
            {"data":[{"id":"gpt-5.6-terra"},{"id":"gpt-4o-mini"},{"id":"text-embedding-3-large"}]}
            """);

        var models = await AzureOpenAIProviderHealthCheck.ReadModelIdsAsync(response, TestContext.Current.CancellationToken);

        models.ShouldBe(["gpt-4o-mini", "gpt-5.6-terra", "text-embedding-3-large"]);
    }

    [Fact]
    public async Task Unexpected_body_returns_an_empty_list()
    {
        using var response = Json("""{"object":"list"}""");

        var models = await AzureOpenAIProviderHealthCheck.ReadModelIdsAsync(response, TestContext.Current.CancellationToken);

        models.ShouldBeEmpty();
    }

    [Fact]
    public async Task Items_without_an_id_are_skipped()
    {
        using var response = Json("""{"data":[{"object":"model"},{"id":""},{"id":"gpt-5.6-terra"}]}""");

        var models = await AzureOpenAIProviderHealthCheck.ReadModelIdsAsync(response, TestContext.Current.CancellationToken);

        models.ShouldBe(["gpt-5.6-terra"]);
    }

    [Fact]
    public async Task Neither_key_nor_address_appears_in_the_detail_for_an_unreachable_endpoint()
    {
        // A closed port: the connection is refused. HttpRequestException.Message
        // would embed the target address in the body; the HttpRequestError
        // category does not carry the address.
        var health = await CheckAsync(o => o.Endpoint = new Uri("http://127.0.0.1:1/"));

        health.Status.ShouldBe(ModelProviderHealthStatus.Unhealthy);
        health.Detail.ShouldNotBeNull();
        health.Detail.ShouldNotContain(TestData.ApiKey);
        health.Detail.ShouldNotContain("127.0.0.1");
    }

    [Fact]
    public async Task Returns_unhealthy_without_making_a_check_call_when_the_address_is_undefined()
    {
        var health = await CheckAsync(o => o.Endpoint = null);

        health.Status.ShouldBe(ModelProviderHealthStatus.Unhealthy);
        health.Latency.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task Public_cloud_scope_is_requested_when_a_credential_factory_is_present()
    {
        var credential = new FakeTokenCredential();

        await CheckAsync(o =>
        {
            o.Endpoint = new Uri("http://127.0.0.1:1/");
            o.CredentialFactory = () => credential;
        });

        credential.LastScope.ShouldBe(AzureOpenAIProviderHealthCheck.DefaultAudience);
    }

    [Fact]
    public async Task Sovereign_cloud_scope_is_read_from_settings()
    {
        var credential = new FakeTokenCredential();
        const string Scope = "https://cognitiveservices.azure.us/.default";

        await CheckAsync(o =>
        {
            o.Endpoint = new Uri("http://127.0.0.1:1/");
            o.Audience = Scope;
            o.CredentialFactory = () => credential;
        });

        credential.LastScope.ShouldBe(Scope);
    }

    [Fact]
    public async Task Credential_is_set_up_once_per_check()
    {
        var credential = new FakeTokenCredential();
        var setupCount = 0;

        var check = new AzureOpenAIProviderHealthCheck(
            AzureOpenAIProviderNames.AzureOpenAI,
            TestData.Options(o =>
            {
                o.Endpoint = new Uri("http://127.0.0.1:1/");
                o.CredentialFactory = () => { setupCount++; return credential; };
            }));

        await check.CheckHealthAsync(TestContext.Current.CancellationToken);
        await check.CheckHealthAsync(TestContext.Current.CancellationToken);

        // The credential OBJECT is set up once (the token cache is
        // preserved), but a new token is requested on every check.
        setupCount.ShouldBe(1);
        credential.RequestedTokenCount.ShouldBe(2);
    }

    [Fact]
    public async Task Provider_without_health_settings_returns_unknown()
    {
        var provider = new AzureOpenAIModelProvider(
            AzureOpenAIProviderNames.AzureOpenAI,
            new AzureOpenAIChatClientFactory(TestData.Options()),
            []);

        var health = await provider.CheckHealthAsync(TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Unknown);
        health.ProviderName.ShouldBe(AzureOpenAIProviderNames.AzureOpenAI);
    }

    private static ValueTask<ModelProviderHealth> CheckAsync(Action<AzureOpenAIProviderOptions> configure)
    {
        var options = TestData.Options(o =>
        {
            o.Timeout = TimeSpan.FromSeconds(5);
            configure(o);
        });

        return new AzureOpenAIProviderHealthCheck(AzureOpenAIProviderNames.AzureOpenAI, options)
            .CheckHealthAsync(TestContext.Current.CancellationToken);
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
}
