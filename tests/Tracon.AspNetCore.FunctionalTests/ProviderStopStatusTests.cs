using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using TraconTestHost = Tracon.AspNetCore.FunctionalTests.Infrastructure.TraconTestHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A run that ends at the output limit of its own agent, or because the provider's filter left nothing to keep, is
/// answered <c>422</c> with its error type on the OpenAI-compatible endpoints, as it is on the agent run endpoint.
/// </summary>
/// <remarks>
/// The provider answered and the request was understood. A <c>502</c> says the provider is broken and gives the caller no
/// error type to tell these from an outage; neither ends differently when the same request is sent again. The agent run
/// endpoint and the replay endpoint have their own cases in <c>OutputTruncationTests</c> and
/// <c>RunReplayEndpointTests</c>; the streamed answers tell the same stop inside the stream, which is unchanged.
/// </remarks>
public sealed class ProviderStopStatusTests
{
    private static readonly Uri ChatCompletions = new("/tracon/v1/chat/completions", UriKind.Relative);
    private static readonly Uri Responses = new("/tracon/v1/responses", UriKind.Relative);

    [Fact]
    public async Task Chat_completions_answers_422_when_a_structured_answer_is_cut_at_the_output_limit()
    {
        var provider = new FinishReasonModelProvider()
            .Text("{\"title\": \"An unfinish", ChatFinishReason.Length, inputTokens: 40, outputTokens: 200);

        await using var host = await OutputTruncationTests.StartAsync(provider, OutputTruncationTests.Structured(maxOutputTokens: 200));

        using var response = await host.Client.PostAsJsonAsync(
            ChatCompletions,
            new { model = OutputTruncationTests.AgentName, messages = new[] { new { role = "user", content = "hello" } } },
            TestContext.Current.CancellationToken);

        await ShouldBeTheEnvelopeAsync(response, TraconOutputTruncatedException.OutputTruncatedErrorType);
    }

    [Fact]
    public async Task Chat_completions_answers_422_when_the_providers_filter_leaves_nothing()
    {
        var provider = new FinishReasonModelProvider()
            .Text(string.Empty, ChatFinishReason.ContentFilter, inputTokens: 900, outputTokens: 0);

        await using var host = await OutputTruncationTests.StartAsync(provider, OutputTruncationTests.PlainText(maxOutputTokens: 50));

        using var response = await host.Client.PostAsJsonAsync(
            ChatCompletions,
            new { model = OutputTruncationTests.AgentName, messages = new[] { new { role = "user", content = "hello" } } },
            TestContext.Current.CancellationToken);

        await ShouldBeTheEnvelopeAsync(response, TraconContentFilteredException.ContentFilteredErrorType);
    }

    [Fact]
    public async Task Responses_answers_422_when_a_structured_answer_is_cut_at_the_output_limit()
    {
        var provider = new FinishReasonModelProvider()
            .Text("{\"title\": \"An unfinish", ChatFinishReason.Length, inputTokens: 40, outputTokens: 200);

        await using var host = await OutputTruncationTests.StartAsync(provider, OutputTruncationTests.Structured(maxOutputTokens: 200));

        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = OutputTruncationTests.AgentName, input = "hello" },
            TestContext.Current.CancellationToken);

        await ShouldBeTheEnvelopeAsync(response, TraconOutputTruncatedException.OutputTruncatedErrorType);
    }

    private static async Task ShouldBeTheEnvelopeAsync(HttpResponseMessage response, string errorType)
    {
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, text);

        var error = System.Text.Json.JsonDocument.Parse(text).RootElement.GetProperty("error");

        error.GetProperty("type").GetString().ShouldBe(errorType);
        error.GetProperty("message").GetString().ShouldNotBeNullOrWhiteSpace();
    }
}
