using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// An answer that arrived and could not be read is a failure a later try can change. Only the ring directly above the
/// provider client knows a JSON failure came out of that client, so the ring marks it and the retry rules read the mark.
/// </summary>
/// <remarks>
/// A JSON failure raised anywhere else (a tool body, a Tracon ring, the writing of a request) never passes through that
/// ring or carries no line number, and keeps the answer it always had: sending the same call again repeats it.
/// </remarks>
public sealed class UnreadableProviderResponseTests
{
    private static readonly ChatMessage[] Messages = [new(ChatRole.User, "hello")];

    [Theory]
    [InlineData("")]
    [InlineData("{\"id\":\"c\",\"choices\":[{\"message\":\"par")]
    [InlineData("<html><body>maintenance</body></html>")]
    public async Task A_body_the_provider_client_could_not_read_is_marked_and_transient(string body)
    {
        var failure = await CatchThroughTheRingAsync(ReadFailure(body));

        FallbackRetryClassifier.Classify(failure).ShouldBe(FallbackSkipReason.UnreadableResponse);
        FallbackRetryClassifier.IsRetryable(failure).ShouldBeTrue();

        var invocation = ProviderInvocationException.UpstreamFailure(failure.Original, "openai", ProviderSecretMask.Empty);

        invocation.IsTransient.ShouldBeTrue();
        invocation.StatusCode.ShouldBeNull();
        invocation.Message.ShouldContain("JsonReaderException", Case.Sensitive, "the message keeps the fault type, which is what the run fingerprint reads");
    }

    [Fact]
    public async Task A_reader_failure_the_client_wrapped_in_its_own_exception_is_marked_too()
    {
        var failure = await CatchThroughTheRingAsync(new InvalidOperationException("The response could not be deserialized.", ReadFailure("<html>")));

        FallbackRetryClassifier.Classify(failure).ShouldBe(FallbackSkipReason.UnreadableResponse);
    }

    [Fact]
    public async Task A_json_failure_that_never_read_a_body_is_not_marked()
    {
        // Only the reader of a body sets a line number; a failure to write a request has a path and nothing else.
        var failure = await CatchThroughTheRingAsync(new JsonException("The request body could not be serialized."));

        FallbackRetryClassifier.Classify(failure).ShouldBe(FallbackSkipReason.None);
        ProviderInvocationException.UpstreamFailure(failure.Original, "openai", ProviderSecretMask.Empty).IsTransient.ShouldBeFalse();
    }

    [Fact]
    public void A_json_failure_that_did_not_pass_the_ring_is_not_classified()
    {
        // A tool's own failure while it reads a document: the tool loop is above the ring and the ring never sees it.
        FallbackRetryClassifier.Classify(ReadFailure("")).ShouldBe(FallbackSkipReason.None);
        FallbackRetryClassifier.IsRetryable(ReadFailure("")).ShouldBeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_permanent_status_in_the_graph_still_wins_over_the_mark(HttpStatusCode status)
    {
        var failure = await CatchThroughTheRingAsync(new HttpRequestException("refused", ReadFailure("<html>"), status));

        FallbackRetryClassifier.Classify(failure).ShouldBe(FallbackSkipReason.None);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, "http_error")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "http_error")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limited")]
    public async Task A_gateway_status_that_came_with_an_unreadable_page_keeps_the_reason_the_status_gives(HttpStatusCode status, string expected)
    {
        var failure = await CatchThroughTheRingAsync(new HttpRequestException("gateway", ReadFailure("<html>"), status));

        FallbackRetryClassifier.Classify(failure).ToWireValue().ShouldBe(expected);
    }

    [Fact]
    public async Task A_byte_position_that_looks_like_a_status_is_not_read_as_one()
    {
        // The reader's message quotes the position it stopped at; a text rule for a rate limit would take 429 for one.
        var body = new string(' ', 429) + "x";
        var read = ReadFailure(body);

        read.Message.ShouldContain("429", Case.Sensitive);

        var failure = await CatchThroughTheRingAsync(read);

        FallbackRetryClassifier.Classify(failure).ShouldBe(FallbackSkipReason.UnreadableResponse);
    }

    [Fact]
    public void A_cancellation_that_carries_a_marked_failure_is_still_a_cancellation()
    {
        var cancellation = new OperationCanceledException("canceled", ReadFailure(""));

        UnreadableProviderResponse.MarkWhenUnreadable(cancellation);

        UnreadableProviderResponse.IsMarked(cancellation).ShouldBeTrue("the failure inside it was read by the mark");
        FallbackRetryClassifier.Classify(cancellation).ShouldBe(FallbackSkipReason.None);
    }

    [Fact]
    public async Task A_failure_while_the_stream_is_read_is_marked()
    {
        using var client = new ProviderFailureTaggingChatClient(new FailingChatClient(ReadFailure("{this is not json}")), ProviderSecretMask.Empty);

        var failure = await Should.ThrowAsync<ForeignProviderInvocationException>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken))
            {
                _ = update;
            }
        });

        FallbackRetryClassifier.Classify(failure).ShouldBe(FallbackSkipReason.UnreadableResponse);
    }

    [Fact]
    public async Task An_unreadable_answer_counts_against_the_provider_like_a_failed_connection()
    {
        var failure = await CatchThroughTheRingAsync(ReadFailure(""));

        CircuitBreakingChatClient.CountsAgainstProvider(failure).ShouldBeTrue();
    }

    [Fact]
    public void The_reason_has_its_own_stable_wire_value()
        => FallbackSkipReason.UnreadableResponse.ToWireValue().ShouldBe("unreadable_response");

    private static async Task<ForeignProviderInvocationException> CatchThroughTheRingAsync(Exception thrownByTheClient)
    {
        using var client = new ProviderFailureTaggingChatClient(new FailingChatClient(thrownByTheClient), ProviderSecretMask.Empty);

        return await Should.ThrowAsync<ForeignProviderInvocationException>(async () =>
            await client.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>The failure a JSON reader raises for a body it cannot read.</summary>
    private static JsonException ReadFailure(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("The body was expected to be unreadable.");
    }

    /// <summary>A provider client that fails with one exception, on a buffered call and at the first read of a stream.</summary>
    private sealed class FailingChatClient(Exception exception) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw exception;

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Failing();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private async IAsyncEnumerable<ChatResponseUpdate> Failing()
        {
            await Task.Yield();

            if (exception is not null)
            {
                throw exception;
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, "never read");
        }
    }
}
