using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// Turning an empty response cut off by a safety/content filter, and a
/// structured response stopped at the output token limit (F-320), into an
/// explicit error.
/// </summary>
/// <remarks>
/// The decorator is applied to every client by <c>ModelProviderRegistry.CreateChatClient</c>;
/// that is why the tests run through the registry — this also catches a break
/// in the wrapping order.
/// </remarks>
public sealed class FinishReasonDetectingChatClientTests
{
    [Fact]
    public async Task Filtered_empty_response_throws_a_content_filtered_error()
    {
        using var chatClient = Registry(new FakeChatClient(_ => new ChatResponse
        {
            Messages = [new ChatMessage(ChatRole.Assistant, string.Empty)],
            FinishReason = ChatFinishReason.ContentFilter,
        }));

        var exception = await Should.ThrowAsync<TraconContentFilteredException>(
            () => chatClient.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken));

        exception.ErrorType.ShouldBe("content_filtered");
        exception.ProviderName.ShouldBe("fake");
        exception.FinishReason.ShouldBe(ChatFinishReason.ContentFilter.Value);
    }

    [Fact]
    public async Task Filtered_response_that_still_carries_text_passes_through()
    {
        // If the model produced text and was then cut off, the user has a
        // partial answer in hand; turning it into an error would be a loss of information.
        using var chatClient = Registry(new FakeChatClient(_ => new ChatResponse
        {
            Messages = [new ChatMessage(ChatRole.Assistant, "partial answer")],
            FinishReason = ChatFinishReason.ContentFilter,
        }));

        var response = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldBe("partial answer");
    }

    [Fact]
    public async Task Unfiltered_empty_response_does_not_throw()
    {
        using var chatClient = Registry(new FakeChatClient(_ => new ChatResponse
        {
            Messages = [new ChatMessage(ChatRole.Assistant, string.Empty)],
            FinishReason = ChatFinishReason.Stop,
        }));

        var response = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task Streaming_filtered_empty_response_throws_at_the_end_of_the_stream()
    {
        using var chatClient = Registry(new FakeChatClient(streamingUpdates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, string.Empty),
            new ChatResponseUpdate(ChatRole.Assistant, string.Empty) { FinishReason = ChatFinishReason.ContentFilter },
        ]));

        await Should.ThrowAsync<TraconContentFilteredException>(async () =>
        {
            await foreach (var _ in chatClient.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "hi")],
                cancellationToken: TestContext.Current.CancellationToken))
            {
                // Frames are consumed; the decision is made at the END of the stream.
            }
        });
    }

    [Fact]
    public async Task Streaming_filtered_response_that_still_carries_text_passes_through()
    {
        using var chatClient = Registry(new FakeChatClient(streamingUpdates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, "partial"),
            new ChatResponseUpdate(ChatRole.Assistant, string.Empty) { FinishReason = ChatFinishReason.ContentFilter },
        ]));

        var text = string.Empty;

        await foreach (var update in chatClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            cancellationToken: TestContext.Current.CancellationToken))
        {
            text += update.Text;
        }

        text.ShouldBe("partial");
    }

    [Fact]
    public async Task Filtered_response_containing_a_tool_call_is_not_counted_as_empty()
    {
        // There is no text but there is a tool call: the response is usable.
        using var chatClient = Registry(new FakeChatClient(_ => new ChatResponse
        {
            Messages = [new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("1", "get_order", null)])],
            FinishReason = ChatFinishReason.ContentFilter,
        }));

        var response = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            cancellationToken: TestContext.Current.CancellationToken);

        // Since Phase 48 the registry also sets up the tool-call loop: MAF adds
        // a result content for an unresolved call. What matters is that NO
        // exception is thrown — a filtered response carrying a tool call is
        // not counted as empty.
        response.Messages
            .SelectMany(static message => message.Contents)
            .OfType<FunctionCallContent>()
            .ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Circuit_breaker_does_not_count_a_filter_as_a_failure()
    {
        // A filtered response shows the provider is HEALTHY. The decorator
        // must sit outside the circuit breaker; if it sat inside, enough
        // filtered requests would trip the circuit and close the provider.
        var breaker = new ModelProviderCircuitBreaker(
            new StaticOptionsMonitor<TraconOptions>(new TraconOptions
            {
                CircuitBreaker = new TraconCircuitBreakerOptions { Enabled = true, FailureThreshold = 2 },
            }));

        var registry = new ModelProviderRegistry(
            [new FakeModelProvider(new FakeChatClient(_ => new ChatResponse
            {
                Messages = [new ChatMessage(ChatRole.Assistant, string.Empty)],
                FinishReason = ChatFinishReason.ContentFilter,
            }))],
            breaker);

        using var chatClient = registry.CreateChatClient(Binding);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Should.ThrowAsync<TraconContentFilteredException>(
                () => chatClient.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken));
        }

        breaker.IsOpen("fake", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Length_on_a_structured_response_throws_output_truncated()
    {
        using var chatClient = Registry(new FakeChatClient(_ => new ChatResponse
        {
            Messages = [new ChatMessage(ChatRole.Assistant, "{\"title\": \"cut")],
            FinishReason = ChatFinishReason.Length,
        }));

        var exception = await Should.ThrowAsync<TraconOutputTruncatedException>(
            () => chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "hi")],
                new ChatOptions { ResponseFormat = ChatResponseFormat.Json, MaxOutputTokens = 200 },
                TestContext.Current.CancellationToken));

        exception.ErrorType.ShouldBe("output_truncated");
        exception.ProviderName.ShouldBe("fake");
        exception.MaxOutputTokens.ShouldBe(200);
        exception.Message.ShouldContain("200");
        exception.Message.ShouldNotContain("title", Case.Sensitive, "the message never carries the response text");
    }

    [Fact]
    public async Task Length_on_a_json_schema_response_without_a_limit_names_the_provider_default()
    {
        using var chatClient = Registry(new FakeChatClient(_ => new ChatResponse
        {
            Messages = [new ChatMessage(ChatRole.Assistant, "{\"a\":")],
            FinishReason = ChatFinishReason.Length,
        }));

        var schema = System.Text.Json.JsonDocument.Parse("{\"type\":\"object\"}").RootElement;

        var exception = await Should.ThrowAsync<TraconOutputTruncatedException>(
            () => chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "hi")],
                new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, "answer") },
                TestContext.Current.CancellationToken));

        exception.MaxOutputTokens.ShouldBeNull();
        exception.Message.ShouldContain("default output token limit");
    }

    [Fact]
    public async Task Length_on_a_plain_text_response_passes_through()
    {
        // K1: a partial text answer is still useful; the call does not fail.
        using var chatClient = Registry(new FakeChatClient(_ => new ChatResponse
        {
            Messages = [new ChatMessage(ChatRole.Assistant, "a partial ans")],
            FinishReason = ChatFinishReason.Length,
        }));

        var response = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            new ChatOptions { ResponseFormat = ChatResponseFormat.Text },
            TestContext.Current.CancellationToken);

        response.Text.ShouldBe("a partial ans");
        response.FinishReason.ShouldBe(ChatFinishReason.Length);
    }

    [Fact]
    public async Task Streaming_length_on_a_structured_response_throws_after_the_last_update()
    {
        using var chatClient = Registry(new FakeChatClient(streamingUpdates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, "{\"a\":"),
            new ChatResponseUpdate(ChatRole.Assistant, " 1") { FinishReason = ChatFinishReason.Length },
        ]));

        var received = new List<string>();

        await Should.ThrowAsync<TraconOutputTruncatedException>(async () =>
        {
            await foreach (var update in chatClient.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "hi")],
                new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
                TestContext.Current.CancellationToken))
            {
                received.Add(update.Text);
            }
        });

        // Every update reached the caller before the decision.
        string.Concat(received).ShouldBe("{\"a\": 1");
    }

    [Fact]
    public async Task Streaming_decision_reads_the_last_finish_reason_not_the_first()
    {
        // A tool-call turn reports its own reason first; the loop then streams
        // the next turn. Only the turn that ends the stream decides.
        using var chatClient = Registry(new FakeChatClient(streamingUpdates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, "{\"a\":") { FinishReason = ChatFinishReason.Length },
            new ChatResponseUpdate(ChatRole.Assistant, "{\"a\": 1}") { FinishReason = ChatFinishReason.Stop },
        ]));

        var count = 0;

        await foreach (var _ in chatClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
            TestContext.Current.CancellationToken))
        {
            count++;
        }

        count.ShouldBe(2, "the stream ended without an exception");
    }

    [Fact]
    public async Task Circuit_breaker_does_not_count_a_truncation_as_a_failure()
    {
        // An output limit shows the provider is healthy, the same as a filter.
        var breaker = new ModelProviderCircuitBreaker(
            new StaticOptionsMonitor<TraconOptions>(new TraconOptions
            {
                CircuitBreaker = new TraconCircuitBreakerOptions { Enabled = true, FailureThreshold = 2 },
            }));

        var registry = new ModelProviderRegistry(
            [new FakeModelProvider(new FakeChatClient(_ => new ChatResponse
            {
                Messages = [new ChatMessage(ChatRole.Assistant, "{")],
                FinishReason = ChatFinishReason.Length,
            }))],
            breaker);

        using var chatClient = registry.CreateChatClient(Binding);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Should.ThrowAsync<TraconOutputTruncatedException>(
                () => chatClient.GetResponseAsync(
                    [new ChatMessage(ChatRole.User, "hi")],
                    new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
                    TestContext.Current.CancellationToken));
        }

        breaker.IsOpen("fake", out _).ShouldBeFalse();
    }

    private static ModelBinding Binding => new() { Provider = "fake", Model = "fake-model" };

    private static IChatClient Registry(FakeChatClient inner)
        => new ModelProviderRegistry([new FakeModelProvider(inner)]).CreateChatClient(Binding);
}
