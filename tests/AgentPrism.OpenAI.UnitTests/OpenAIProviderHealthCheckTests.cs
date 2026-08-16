using System.Net;
using System.Text;

namespace AgentPrism.OpenAI.UnitTests;

/// <summary>
/// Verifies the pure parts of <see cref="OpenAIProviderHealthCheck"/> that need no
/// network call: address joining and JSON parsing.
/// </summary>
/// <remarks>
/// The full end-to-end path (a real HTTP GET, timeout, error body) is verified
/// against a real local server in the functional tests — see
/// <c>ModelHealthEndpointsTests</c> and <c>OpenAiCompatibleLocalServerTests</c>.
/// </remarks>
public sealed class OpenAIProviderHealthCheckTests
{
    [Theory]
    [InlineData("https://openrouter.example.com/api/v1", "https://openrouter.example.com/api/v1/models")]
    [InlineData("https://openrouter.example.com/api/v1/", "https://openrouter.example.com/api/v1/models")]
    [InlineData("http://localhost:11434/v1", "http://localhost:11434/v1/models")]
    public void Base_address_joins_correctly_regardless_of_the_trailing_slash(string baseEndpoint, string expected)
    {
        var result = OpenAIProviderHealthCheck.BuildModelsEndpoint(new Uri(baseEndpoint));

        result.ShouldBe(new Uri(expected));
    }

    [Fact]
    public void Official_OpenAI_address_is_used_when_no_address_is_given()
    {
        var result = OpenAIProviderHealthCheck.BuildModelsEndpoint(null);

        result.ShouldBe(new Uri("https://api.openai.com/v1/models"));
    }

    [Fact]
    public async Task Model_ids_are_read_from_the_data_array_in_sorted_order()
    {
        using var response = JsonResponse("""{"data":[{"id":"b"},{"id":"a"}]}""");

        var models = await OpenAIProviderHealthCheck.ReadModelIdsAsync(response, CancellationToken.None);

        models.ShouldBe(["a", "b"]);
    }

    [Fact]
    public async Task Empty_list_is_returned_when_the_data_field_is_missing()
    {
        using var response = JsonResponse("""{"object":"list"}""");

        var models = await OpenAIProviderHealthCheck.ReadModelIdsAsync(response, CancellationToken.None);

        models.ShouldBeEmpty();
    }

    [Fact]
    public async Task Empty_list_is_returned_when_data_is_not_an_array()
    {
        using var response = JsonResponse("""{"data":"unexpected"}""");

        var models = await OpenAIProviderHealthCheck.ReadModelIdsAsync(response, CancellationToken.None);

        models.ShouldBeEmpty();
    }

    [Fact]
    public async Task Entries_without_an_id_field_are_skipped()
    {
        using var response = JsonResponse("""{"data":[{"id":"valid"},{"owned_by":"x"},{"id":123}]}""");

        var models = await OpenAIProviderHealthCheck.ReadModelIdsAsync(response, CancellationToken.None);

        models.ShouldBe(["valid"]);
    }

    [Fact]
    public async Task Model_count_is_capped_at_200()
    {
        var entries = string.Join(',', Enumerable.Range(0, 250).Select(static i => $$"""{"id":"model-{{i}}"}"""));
        using var response = JsonResponse($$"""{"data":[{{entries}}]}""");

        var models = await OpenAIProviderHealthCheck.ReadModelIdsAsync(response, CancellationToken.None);

        models.Count.ShouldBe(200);
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
}
