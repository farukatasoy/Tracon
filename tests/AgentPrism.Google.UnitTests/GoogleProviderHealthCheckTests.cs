using System.Net;
using System.Text;
using AgentPrism.Google.UnitTests.Infrastructure;

namespace AgentPrism.Google.UnitTests;

/// <summary>
/// The health check's endpoint join and response parsing. No network call is made.
/// </summary>
public sealed class GoogleProviderHealthCheckTests
{
    [Fact]
    public void No_address_uses_the_official_endpoint_and_default_version()
        => GoogleProviderHealthCheck.BuildModelsEndpoint(null, null)
            .ToString().ShouldBe("https://generativelanguage.googleapis.com/v1beta/models");

    [Fact]
    public void Api_version_is_included_in_the_address_when_given()
        => GoogleProviderHealthCheck.BuildModelsEndpoint(null, "v1")
            .ToString().ShouldBe("https://generativelanguage.googleapis.com/v1/models");

    [Fact]
    public void Base_address_without_trailing_slash_does_not_swallow_the_last_segment()
        => GoogleProviderHealthCheck.BuildModelsEndpoint(new Uri("https://example.test/genai"), "v1beta")
            .ToString().ShouldBe("https://example.test/genai/v1beta/models");

    [Fact]
    public async Task Model_names_are_read_from_the_response_and_the_prefix_is_stripped()
    {
        using var response = Json("""
            {"models":[{"name":"models/gemini-3.6-flash"},{"name":"models/gemini-3.1-pro-preview"}]}
            """);

        var models = await GoogleProviderHealthCheck.ReadModelIdsAsync(response, TestContext.Current.CancellationToken);

        // The ModelBinding.Model field does not carry the "models/" prefix; the prefix must be stripped.
        models.ShouldBe(["gemini-3.1-pro-preview", "gemini-3.6-flash"]);
    }

    [Fact]
    public async Task Name_without_a_prefix_is_left_unchanged()
    {
        using var response = Json("""{"models":[{"name":"custom-model"}]}""");

        var models = await GoogleProviderHealthCheck.ReadModelIdsAsync(response, TestContext.Current.CancellationToken);

        models.ShouldBe(["custom-model"]);
    }

    [Fact]
    public async Task Unexpected_body_returns_an_empty_list()
    {
        // OpenAI's shape ("data") is not valid for Gemini; it must return empty silently.
        using var response = Json("""{"data":[{"id":"gemini-3.6-flash"}]}""");

        var models = await GoogleProviderHealthCheck.ReadModelIdsAsync(response, TestContext.Current.CancellationToken);

        models.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unreachable_endpoint_detail_contains_neither_the_key_nor_the_address()
    {
        var options = TestData.Options(o =>
        {
            o.Endpoint = new Uri("http://127.0.0.1:1");
            o.Timeout = TimeSpan.FromSeconds(5);
        });

        var health = await new GoogleProviderHealthCheck(GoogleProviderNames.Google, options)
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Unhealthy);
        health.Detail.ShouldNotBeNull();
        health.Detail.ShouldNotContain(TestData.ApiKey);
        health.Detail.ShouldNotContain("127.0.0.1");
    }

    [Fact]
    public async Task Provider_without_health_settings_returns_unknown()
    {
        using var factory = new GoogleChatClientFactory(TestData.Options());
        var provider = new GoogleModelProvider(GoogleProviderNames.Google, factory, []);

        var health = await provider.CheckHealthAsync(TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Unknown);
        health.ProviderName.ShouldBe(GoogleProviderNames.Google);
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
}
