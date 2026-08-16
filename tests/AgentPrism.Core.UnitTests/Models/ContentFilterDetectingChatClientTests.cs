using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Models;

/// <summary>
/// Turning an empty response cut off by a safety/content filter into an explicit error.
/// </summary>
/// <remarks>
/// The decorator is applied to every client by <c>ModelProviderRegistry.CreateChatClient</c>;
/// that is why the tests run through the registry — this also catches a break
/// in the wrapping order.
/// </remarks>
public sealed class ContentFilterDetectingChatClientTests
{
    [Fact]
    public async Task Filtered_empty_response_throws_a_content_filtered_error()
    {
        using var chatClient = Registry(new FakeChatClient(_ => new ChatResponse
        {
            Messages = [new ChatMessage(ChatRole.Assistant, string.Empty)],
            FinishReason = ChatFinishReason.ContentFilter,
        }));

        var exception = await Should.ThrowAsync<AgentPrismContentFilteredException>(
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

        await Should.ThrowAsync<AgentPrismContentFilteredException>(async () =>
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
            new StaticOptionsMonitor<AgentPrismOptions>(new AgentPrismOptions
            {
                CircuitBreaker = new AgentPrismCircuitBreakerOptions { Enabled = true, FailureThreshold = 2 },
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
            await Should.ThrowAsync<AgentPrismContentFilteredException>(
                () => chatClient.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken));
        }

        breaker.IsOpen("fake", out _).ShouldBeFalse();
    }

    private static ModelBinding Binding => new() { Provider = "fake", Model = "fake-model" };

    private static IChatClient Registry(FakeChatClient inner)
        => new ModelProviderRegistry([new FakeModelProvider(inner)]).CreateChatClient(Binding);
}
