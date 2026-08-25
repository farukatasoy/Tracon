using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism.Core.UnitTests.Models;

public sealed class ModelProviderFailureNormalizationTests
{
    private const string SecretLikeMessage =
        "https://tenant-private.example; Authorization=Bearer sk-secret-preview1; account=acct-42";

    [Fact]
    public void Construction_failure_is_normalized_and_the_original_exception_is_logged()
    {
        var original = new ForeignProviderException(SecretLikeMessage);
        var logs = new ExceptionRecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var registry = new ModelProviderRegistry([new ConstructionFailingProvider(original)], loggerFactory: loggerFactory);

        var exception = Should.Throw<AgentPrismException>(() => registry.CreateChatClient(TestData.Binding()));

        exception.ErrorType.ShouldBe("upstream_error");
        exception.Message.ShouldNotContain(SecretLikeMessage);
        exception.InnerException.ShouldBeSameAs(original);
        logs.Entries.ShouldContain(entry =>
            string.Equals(entry.Category, "AgentPrism.ModelProvider", StringComparison.Ordinal)
            && ReferenceEquals(entry.Exception, original));
    }

    [Fact]
    public async Task Buffered_and_streaming_invocation_failures_are_normalized_after_the_pipeline()
    {
        var original = new ForeignProviderException(SecretLikeMessage);
        var logs = new ExceptionRecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        using var client = new ModelProviderRegistry(
            [new FixedClientProvider(new FailingClient(original))],
            loggerFactory: loggerFactory)
            .CreateChatClient(TestData.Binding());

        var buffered = await Should.ThrowAsync<AgentPrismException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]));
        var streaming = await Should.ThrowAsync<AgentPrismException>(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")]))
            {
            }
        });

        buffered.ErrorType.ShouldBe("upstream_error");
        streaming.ErrorType.ShouldBe("upstream_error");
        buffered.Message.ShouldNotContain(SecretLikeMessage);
        streaming.Message.ShouldNotContain(SecretLikeMessage);
        logs.Entries.Count(entry =>
            string.Equals(entry.Category, "AgentPrism.ModelProvider", StringComparison.Ordinal)
            && ReferenceEquals(entry.Exception, original)).ShouldBe(2);
    }

    [Fact]
    public async Task Known_safe_AgentPrism_error_and_caller_cancellation_are_preserved()
    {
        var safe = new AgentPrismContentBlockedException("The request was blocked safely.");
        using var safeClient = new ModelProviderRegistry([new FixedClientProvider(new FailingClient(safe))])
            .CreateChatClient(TestData.Binding());

        var observed = await Should.ThrowAsync<AgentPrismContentBlockedException>(
            () => safeClient.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]));
        observed.ShouldBeSameAs(safe);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var cancelledClient = new ModelProviderRegistry(
            [new FixedClientProvider(new FailingClient(new OperationCanceledException(cancellation.Token)))])
            .CreateChatClient(TestData.Binding());

        await Should.ThrowAsync<OperationCanceledException>(
            () => cancelledClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "hello")],
                cancellationToken: cancellation.Token));
    }

    private sealed class ConstructionFailingProvider(Exception exception) : IModelProvider
    {
        public string Name => "fake";

        public IReadOnlyList<ModelDescriptor> Models { get; } = [];

        public IChatClient CreateChatClient(ModelBinding binding) => throw exception;
    }

    private sealed class FixedClientProvider(IChatClient client) : IModelProvider
    {
        public string Name => "fake";

        public IReadOnlyList<ModelDescriptor> Models { get; } = [];

        public IChatClient CreateChatClient(ModelBinding binding) => client;
    }

    private sealed class FailingClient(Exception exception) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromException<ChatResponse>(exception);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw exception;
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ForeignProviderException(string message) : Exception(message);

    private sealed class ExceptionRecordingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(string Category, Exception Exception)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new ExceptionRecordingLogger(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class ExceptionRecordingLogger(
            string category,
            ConcurrentQueue<(string Category, Exception Exception)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (exception is not null)
                {
                    entries.Enqueue((category, exception));
                }
            }
        }
    }
}
