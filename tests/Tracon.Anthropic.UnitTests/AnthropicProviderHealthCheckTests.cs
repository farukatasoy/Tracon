using System.Net;
using System.Text;
using Tracon.Anthropic.UnitTests.Infrastructure;

namespace Tracon.Anthropic.UnitTests;

/// <summary>
/// The health check's address joining and response parsing. No network call is made.
/// </summary>
public sealed class AnthropicProviderHealthCheckTests
{
    [Fact]
    public void No_address_given_falls_back_to_the_official_endpoint()
        => AnthropicProviderHealthCheck.BuildModelsEndpoint(null)
            .ToString().ShouldBe("https://api.anthropic.com/v1/models");

    [Fact]
    public void Base_address_without_a_trailing_slash_does_not_swallow_the_last_segment()
        => AnthropicProviderHealthCheck.BuildModelsEndpoint(new Uri("https://example.gateway/v1"))
            .ToString().ShouldBe("https://example.gateway/v1/models");

    [Fact]
    public void Base_address_with_a_trailing_slash_gives_the_same_result()
        => AnthropicProviderHealthCheck.BuildModelsEndpoint(new Uri("https://example.gateway/v1/"))
            .ToString().ShouldBe("https://example.gateway/v1/models");

    [Fact]
    public async Task Model_ids_are_read_from_the_response_and_sorted()
    {
        using var response = Json("""
            {"data":[{"id":"claude-sonnet-5"},{"id":"claude-haiku-4-5-20251001"},{"id":"claude-opus-5"}]}
            """);

        var models = await AnthropicProviderHealthCheck.ReadModelIdsAsync(response, TestContext.Current.CancellationToken);

        models.ShouldBe(["claude-haiku-4-5-20251001", "claude-opus-5", "claude-sonnet-5"]);
    }

    [Fact]
    public async Task Unexpected_body_returns_an_empty_list()
    {
        using var response = Json("""{"object":"list"}""");

        var models = await AnthropicProviderHealthCheck.ReadModelIdsAsync(response, TestContext.Current.CancellationToken);

        models.ShouldBeEmpty();
    }

    [Fact]
    public async Task Idless_entries_are_skipped()
    {
        using var response = Json("""{"data":[{"object":"model"},{"id":""},{"id":"claude-opus-5"}]}""");

        var models = await AnthropicProviderHealthCheck.ReadModelIdsAsync(response, TestContext.Current.CancellationToken);

        models.ShouldBe(["claude-opus-5"]);
    }

    [Fact]
    public async Task Unreachable_endpoint_detail_shows_neither_the_key_nor_the_address()
    {
        // A closed port: the connection is refused. HttpRequestException.Message
        // would embed the target address in the body; the HttpRequestError category
        // carries no address.
        var options = TestData.Options(o =>
        {
            o.Endpoint = new Uri("http://127.0.0.1:1/v1");
            o.Timeout = TimeSpan.FromSeconds(5);
        });

        var health = await new AnthropicProviderHealthCheck(AnthropicProviderNames.Anthropic, options)
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Unhealthy);
        health.Detail.ShouldNotBeNull();
        health.Detail.ShouldNotContain(TestData.ApiKey);
        health.Detail.ShouldNotContain("127.0.0.1");
    }

    [Fact]
    public async Task Provider_without_health_settings_returns_unknown()
    {
        var provider = new AnthropicModelProvider(
            AnthropicProviderNames.Anthropic,
            new AnthropicChatClientFactory(TestData.Options()),
            []);

        var health = await provider.CheckHealthAsync(TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Unknown);
        health.ProviderName.ShouldBe(AnthropicProviderNames.Anthropic);
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
}
