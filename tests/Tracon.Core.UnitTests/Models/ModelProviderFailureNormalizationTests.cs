using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Models;

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

        var exception = Should.Throw<TraconException>(() => registry.CreateChatClient(TestData.Binding()));

        exception.ErrorType.ShouldBe("upstream_error");
        exception.Message.ShouldNotContain(SecretLikeMessage);
        exception.InnerException.ShouldBeSameAs(original);
        logs.Entries.ShouldContain(entry =>
            string.Equals(entry.Category, "Tracon.ModelProvider", StringComparison.Ordinal)
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

        var buffered = await Should.ThrowAsync<TraconException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]));
        var streaming = await Should.ThrowAsync<TraconException>(async () =>
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
            string.Equals(entry.Category, "Tracon.ModelProvider", StringComparison.Ordinal)
            && ReferenceEquals(entry.Exception, original)).ShouldBe(2);
    }

    [Fact]
    public async Task Known_safe_Tracon_error_and_caller_cancellation_are_preserved()
    {
        var safe = new TraconContentBlockedException("The request was blocked safely.");
        using var safeClient = new ModelProviderRegistry([new FixedClientProvider(new FailingClient(safe))])
            .CreateChatClient(TestData.Binding());

        var observed = await Should.ThrowAsync<TraconContentBlockedException>(
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

    [Fact]
    public void A_value_outside_its_set_that_a_provider_rejects_with_the_shipped_reader_reaches_the_caller_by_key_and_never_by_value()
    {
        const string Value = "turbo-canary-value-4f2a";
        var registry = new ModelProviderRegistry([new ChoiceProvider()]);

        var exception = Should.Throw<TraconException>(() => registry.CreateChatClient(Choice(Value)));

        string.Equals(exception.ErrorType, "upstream_error", StringComparison.Ordinal)
            .ShouldBeFalse("the registry hid a rejection the provider wrote for the caller");
        exception.Message.ShouldContain("acme.mode", Case.Sensitive);
        exception.Message.ShouldContain("fast, slow", Case.Sensitive);
        exception.Message.ShouldNotContain(Value);
    }

    [Fact]
    public void A_TraconException_a_foreign_provider_raises_by_itself_is_replaced_by_the_generic_upstream_error()
    {
        // Why ReadChoice exists: the registry cannot tell a message a provider wrote for the caller from one that holds an
        // address or a token, so only the exception types Tracon raises itself are let through.
        var registry = new ModelProviderRegistry([new ConstructionFailingProvider(new TraconException("acme.mode is not one of: fast, slow"))]);

        var exception = Should.Throw<TraconException>(() => registry.CreateChatClient(TestData.Binding()));

        exception.ErrorType.ShouldBe("upstream_error");
        exception.Message.ShouldNotContain("acme.mode", Case.Sensitive);
    }

    [Fact]
    public void A_value_inside_its_set_is_returned_as_the_provider_spells_it_whatever_the_case_the_caller_wrote()
    {
        var registry = new ModelProviderRegistry([new ChoiceProvider()]);

        using var client = registry.CreateChatClient(Choice("  SLOW "));

        client.GetService(typeof(ChoiceProvider.Chosen)).ShouldBeOfType<ChoiceProvider.Chosen>().Mode.ShouldBe("slow");
    }

    [Fact]
    public void A_setting_that_is_not_set_reads_as_null_and_a_value_that_is_not_text_is_refused_by_kind()
    {
        ModelProviderSettings.ReadChoice(TestData.Binding(), "acme.mode", ["fast", "slow"]).ShouldBeNull();

        var number = TestData.Binding() with
        {
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["acme.mode"] = JsonSerializer.SerializeToElement(7),
            },
        };

        Should.Throw<TraconException>(() => ModelProviderSettings.ReadChoice(number, "acme.mode", ["fast", "slow"]))
            .Message.ShouldContain("expects text", Case.Sensitive);
    }

    [Fact]
    public void A_set_with_nothing_in_it_is_a_programming_error_and_not_a_user_error()
        => Should.Throw<ArgumentException>(() => ModelProviderSettings.ReadChoice(TestData.Binding(), "acme.mode", []));

    private static ModelBinding Choice(string value)
        => TestData.Binding() with
        {
            Provider = "choice",
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["acme.mode"] = JsonSerializer.SerializeToElement(value),
            },
        };

    private sealed class ChoiceProvider : IModelProvider
    {
        public string Name => "choice";

        public IReadOnlyList<ModelDescriptor> Models { get; } = [];

        public IChatClient CreateChatClient(ModelBinding binding)
            => new Chosen(ModelProviderSettings.ReadChoice(binding, "acme.mode", ["fast", "slow"]));

        internal sealed class Chosen(string? mode) : IChatClient
        {
            public string? Mode { get; } = mode;

            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
                => Task.FromResult(new ChatResponse());

            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public object? GetService(Type serviceType, object? serviceKey = null)
                => serviceType == typeof(Chosen) ? this : null;

            public void Dispose()
            {
            }
        }
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
