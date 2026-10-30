using Tracon.OpenAI.UnitTests.Infrastructure;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// What the OpenAI provider states through <see cref="ISamplingSupport.SamplingLimits"/>: the four stop
/// sequences OpenAI documents for Chat Completions, for OpenAI's own service only.
/// </summary>
/// <remarks>
/// A gateway or a compatible server can accept more than OpenAI does, and a limit that is too narrow would reject a
/// definition that runs there. So a provider with another address states none, as the strict-schema refusals
/// (<see cref="OpenAIResponseSchemaSupportTests"/>) are listed only for the service they were measured against.
/// The seed is a 64-bit integer on the wire, so no seed range is stated for either surface.
/// </remarks>
public sealed class OpenAISamplingLimitsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("https://api.openai.com/v1/")]
    [InlineData("https://API.OPENAI.COM/v1")]
    public void Chat_Completions_on_the_OpenAI_service_accepts_four_stop_sequences_and_any_seed(string? endpoint)
    {
        var limits = Provider(endpoint is null ? null : new Uri(endpoint)).SamplingLimits;

        limits.MaxStopSequences.ShouldBe(4);
        limits.MinSeed.ShouldBeNull();
        limits.MaxSeed.ShouldBeNull();
    }

    [Fact]
    public void A_provider_built_without_options_is_the_default_OpenAI_service()
    {
        var provider = new OpenAIModelProvider(
            OpenAIProviderNames.ChatCompletions,
            OpenAIApiSurface.ChatCompletions,
            new OpenAIChatClientFactory(TestData.Options()),
            []);

        provider.SamplingLimits.MaxStopSequences.ShouldBe(4);
    }

    [Theory]
    [InlineData("http://127.0.0.1:11434/v1")]
    [InlineData("https://openrouter.ai/api/v1")]
    [InlineData("https://my-gateway.example.com/openai/v1")]
    [InlineData("https://notapi.openai.com.example.net/v1")]
    public void A_server_at_another_address_states_no_limit_because_none_was_documented_for_it(string endpoint)
        => Provider(new Uri(endpoint)).SamplingLimits.ShouldBe(SamplingLimits.None);

    [Theory]
    [InlineData(null)]
    [InlineData("https://api.openai.com/v1/")]
    public void The_Responses_API_sends_no_stop_sequence_and_states_no_limit(string? endpoint)
        => Provider(endpoint is null ? null : new Uri(endpoint), responses: true).SamplingLimits.ShouldBe(SamplingLimits.None);

    private static OpenAIModelProvider Provider(Uri? endpoint, bool responses = false)
    {
        var options = TestData.Options(o => o.Endpoint = endpoint);

        return new OpenAIModelProvider(
            responses ? OpenAIProviderNames.Responses : OpenAIProviderNames.ChatCompletions,
            responses ? OpenAIApiSurface.Responses : OpenAIApiSurface.ChatCompletions,
            new OpenAIChatClientFactory(options),
            [],
            healthCheckOptions: options);
    }
}
