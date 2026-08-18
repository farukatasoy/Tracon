using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Models;

/// <summary>
/// Verifies the <see cref="ModelBinding.Fallbacks"/> chain: when it retries,
/// when it does not, model attribution, and the chain-exhausted error.
/// </summary>
/// <remarks>
/// Tests run through <see cref="ModelProviderRegistry"/>, not
/// <see cref="FallbackChatClient"/> directly — this also proves the wrapping
/// POSITION (K-320's rule applied to phase 62): outside the circuit breaker,
/// inside content-filter detection.
/// </remarks>
public sealed class FallbackChatClientTests
{
    private static readonly ChatMessage[] Messages = [new ChatMessage(ChatRole.User, "hi")];

    [Fact]
    public async Task Empty_fallbacks_preserves_todays_error_path()
    {
        var primary = new FakeModelProvider(
            new FakeChatClient(_ => throw new AgentPrismProviderUnavailableException("circuit open") { ProviderName = "primary" }),
            name: "primary");

        using var chatClient = new ModelProviderRegistry([primary]).CreateChatClient(Binding(primary: "primary"));

        var exception = await Should.ThrowAsync<AgentPrismProviderUnavailableException>(
            () => chatClient.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken));

        exception.ProviderName.ShouldBe("primary");
        chatClient.GetService(typeof(FallbackChatClient)).ShouldBeNull();
    }

    [Fact]
    public async Task Open_circuit_on_primary_falls_over_to_the_fallback()
    {
        var primaryClient = new FakeChatClient(
            _ => throw new AgentPrismProviderUnavailableException("circuit open") { ProviderName = "primary" });
        var fallbackClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "fallback answer")));

        using var chatClient = RegistryWithFallback(primaryClient, fallbackClient);

        var response = await chatClient.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldBe("fallback answer");
        fallbackClient.CallCount.ShouldBe(1);
    }

    [Theory]
    [InlineData("HTTP 500 (internal_server_error)")]
    [InlineData("HTTP 503 (service_unavailable)")]
    [InlineData("HTTP 429 (rate_limit_exceeded)")]
    [InlineData("rate limit exceeded, please retry later")]
    public async Task Retryable_provider_errors_fall_over(string message)
    {
        var primaryClient = new FakeChatClient(_ => throw new InvalidOperationException(message));
        var fallbackClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "fallback answer")));

        using var chatClient = RegistryWithFallback(primaryClient, fallbackClient);

        var response = await chatClient.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldBe("fallback answer");
    }

    [Fact]
    public async Task A_bare_connection_failure_wrapped_the_way_the_real_OpenAI_client_wraps_it_falls_over()
    {
        // Regression for the phase 62 audit finding: measured against the
        // real OpenAI 2.12.0 client pointed at a port nothing listens on,
        // the exception that reaches this layer is an AggregateException
        // ("Retry failed after 4 tries...") whose InnerException is
        // System.ClientModel.ClientResultException ("Connection refused
        // (...)") — a message with NO "HTTP nnn" text, because no response
        // was ever received. Neither the outer nor the inner message alone
        // matched the old (unflattened, bare-type-only) classifier.
        var connectionFailure = new AggregateException(
            "Retry failed after 4 tries. (Connection refused (127.0.0.1:1))",
            new FakeClientResultException("Connection refused (127.0.0.1:1)"));

        var primaryClient = new FakeChatClient(_ => throw connectionFailure);
        var fallbackClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "fallback answer")));

        using var chatClient = RegistryWithFallback(primaryClient, fallbackClient);

        var response = await chatClient.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldBe("fallback answer");
    }

    [Fact]
    public async Task An_authentication_failure_wrapped_the_same_way_still_does_not_retry()
    {
        var authFailure = new AggregateException(
            "Retry failed after 4 tries. (HTTP 401 (invalid_api_key))",
            new FakeClientResultException("HTTP 401 (invalid_api_key)"));

        var primaryClient = new FakeChatClient(_ => throw authFailure);
        var fallbackClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "fallback answer")));

        using var chatClient = RegistryWithFallback(primaryClient, fallbackClient);

        await Should.ThrowAsync<AggregateException>(
            () => chatClient.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken));

        fallbackClient.CallCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("HTTP 401 (invalid_api_key)")]
    [InlineData("HTTP 403 (forbidden)")]
    public async Task Authentication_errors_are_not_retried(string message)
    {
        var primaryClient = new FakeChatClient(_ => throw new InvalidOperationException(message));
        var fallbackClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "fallback answer")));

        using var chatClient = RegistryWithFallback(primaryClient, fallbackClient);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => chatClient.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken));

        exception.Message.ShouldBe(message);
        fallbackClient.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Canceled_calls_are_not_retried()
    {
        using var cts = new CancellationTokenSource();
        var primaryClient = new FakeChatClient(_ => throw new OperationCanceledException(cts.Token));
        var fallbackClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "fallback answer")));

        using var chatClient = RegistryWithFallback(primaryClient, fallbackClient);

        await Should.ThrowAsync<OperationCanceledException>(
            () => chatClient.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken));

        fallbackClient.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Content_filter_does_not_trigger_a_fallback_attempt()
    {
        // A provider-filtered response is a NORMAL return value at this layer
        // (ContentFilterDetectingChatClient sits OUTSIDE the fallback chain and
        // converts it to an exception only after the fallback client already
        // returned) — the fallback link must never even be called.
        var primaryClient = new FakeChatClient(_ => new ChatResponse
        {
            Messages = [new ChatMessage(ChatRole.Assistant, string.Empty)],
            FinishReason = ChatFinishReason.ContentFilter,
        });
        var fallbackClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "fallback answer")));

        using var chatClient = RegistryWithFallback(primaryClient, fallbackClient);

        await Should.ThrowAsync<AgentPrismContentFilteredException>(
            () => chatClient.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken));

        fallbackClient.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Exhausted_chain_throws_the_first_failure_not_the_last()
    {
        var primaryClient = new FakeChatClient(_ => throw new InvalidOperationException("HTTP 500 (primary down)"));
        var fallbackClient = new FakeChatClient(_ => throw new InvalidOperationException("HTTP 503 (fallback also down)"));

        using var chatClient = RegistryWithFallback(primaryClient, fallbackClient);

        var exception = await Should.ThrowAsync<AgentPrismProviderUnavailableException>(
            () => chatClient.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken));

        exception.InnerException!.Message.ShouldBe("HTTP 500 (primary down)");
        exception.Message.ShouldContain("primary");
        exception.Message.ShouldContain("fallback");
        exception.Message.ShouldContain("HTTP 500 (primary down)");
    }

    [Fact]
    public async Task Fallback_used_is_recorded_on_the_ambient_run_scope()
    {
        var primaryClient = new FakeChatClient(_ => throw new AgentPrismProviderUnavailableException("circuit open"));
        var fallbackClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "fallback answer")));

        using var chatClient = RegistryWithFallback(primaryClient, fallbackClient);

        var runStore = new InMemoryRunStore();
        var runId = Guid.NewGuid();
        var writer = new RunEventWriter(
            runStore, new AgentPrismRunRecordingOptions(), NullLogger.Instance, runId);
        var attribution = new FallbackModelAttribution();

        AgentPrismRunContext.SetCurrent(new AgentRunScope
        {
            RunId = runId,
            RootRunId = runId,
            Writer = writer,
            FallbackAttribution = attribution,
        });

        try
        {
            await chatClient.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);
        }
        finally
        {
            AgentPrismRunContext.SetCurrent(null);
        }

        attribution.Current.ShouldBe(("fallback", "fallback-model"));
    }

    [Fact]
    public async Task Streaming_open_circuit_before_the_first_chunk_falls_over()
    {
        var primary = new FakeModelProvider(
            FailingStreamClient(new AgentPrismProviderUnavailableException("circuit open")),
            name: "primary");
        var fallback = new FakeModelProvider(
            new FakeChatClient(streamingUpdates: [new ChatResponseUpdate(ChatRole.Assistant, "fallback chunk")]),
            name: "fallback");

        using var chatClient = new ModelProviderRegistry([primary, fallback]).CreateChatClient(
            Binding(primary: "primary", fallbackProvider: "fallback", fallbackModel: "fallback-model"));

        var chunks = new List<string>();

        await foreach (var update in chatClient.GetStreamingResponseAsync(
            Messages, cancellationToken: TestContext.Current.CancellationToken))
        {
            chunks.Add(update.Text);
        }

        chunks.ShouldBe(["fallback chunk"]);
    }

    [Fact]
    public async Task Streaming_failure_after_the_first_chunk_is_never_retried()
    {
        var primary = new FakeModelProvider(
            FailingAfterFirstChunkClient(),
            name: "primary");
        var fallback = new FakeModelProvider(
            new FakeChatClient(streamingUpdates: [new ChatResponseUpdate(ChatRole.Assistant, "should never be reached")]),
            name: "fallback");

        using var chatClient = new ModelProviderRegistry([primary, fallback]).CreateChatClient(
            Binding(primary: "primary", fallbackProvider: "fallback", fallbackModel: "fallback-model"));

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in chatClient.GetStreamingResponseAsync(
                Messages, cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        });
    }

    private static ThrowingStreamChatClient FailingStreamClient(Exception exception) => new(exception, sawUpdateFirst: false);

    private static ThrowingStreamChatClient FailingAfterFirstChunkClient()
        => new(new InvalidOperationException("HTTP 500 (mid-stream)"), sawUpdateFirst: true);

    private static IChatClient RegistryWithFallback(FakeChatClient primaryClient, FakeChatClient fallbackClient)
    {
        var primary = new FakeModelProvider(primaryClient, name: "primary");
        var fallback = new FakeModelProvider(fallbackClient, name: "fallback");

        return new ModelProviderRegistry([primary, fallback]).CreateChatClient(
            Binding(primary: "primary", fallbackProvider: "fallback", fallbackModel: "fallback-model"));
    }

    private static ModelBinding Binding(
        string primary,
        string primaryModel = "primary-model",
        string? fallbackProvider = null,
        string? fallbackModel = null)
        => new()
        {
            Provider = primary,
            Model = primaryModel,
            Fallbacks = fallbackProvider is null
                ? []
                : [new ModelFallback { Provider = fallbackProvider, Model = fallbackModel! }],
        };

    /// <summary>A fake streaming client that yields (or not) then throws, for testing the "already streamed" rule.</summary>
    /// <summary>Mimics <c>System.ClientModel.ClientResultException</c> BY NAME, for classifier tests that cannot reference the real SDK type.</summary>
    private sealed class FakeClientResultException(string message) : Exception(message);

    private sealed class ThrowingStreamChatClient(Exception exception, bool sawUpdateFirst) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (sawUpdateFirst)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "first chunk");
                await Task.Yield();
            }

            throw exception;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
