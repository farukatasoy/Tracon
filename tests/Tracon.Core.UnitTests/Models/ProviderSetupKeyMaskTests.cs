using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// The keys a provider reports it was set up with are replaced in the text of its failures, and only those.
/// </summary>
/// <remarks>
/// The tests build a registry over scripted providers: a client that throws what a model service throws when it
/// quotes the key it refused. What reaches the caller, the loggers and the fallback chain is read as a writer
/// reads it, the exception graph included.
/// </remarks>
public sealed class ProviderSetupKeyMaskTests
{
    private const string PrimaryKey = "canary-setup-primary-8a41c0d7";
    private const string SecondKey = "canary-setup-second-31f9be52";
    private const string FallbackKey = "canary-setup-fallback-c7064e1a";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_key_the_provider_reports_is_replaced_in_every_text_of_its_failure(bool streamed)
    {
        var raw = new InvalidOperationException($"HTTP 401 Incorrect API key provided: Bearer {PrimaryKey}.");
        using var logs = new CapturedLogs();
        using var client = Registry(logs, new ScriptedProvider("keyed", [PrimaryKey], raw)).CreateChatClient(Binding("keyed"));

        var thrown = await Should.ThrowAsync<ProviderInvocationException>(() => CallAsync(client, streamed));

        Written(thrown).ShouldNotContain(PrimaryKey, Case.Sensitive);
        Written(thrown).ShouldContain("Incorrect API key provided: Bearer [redacted].");
        thrown.InnerException.ShouldBeOfType<MaskedFailureView>();
        thrown.Original.ShouldBeSameAs(raw);
        logs.Text.ShouldNotContain(PrimaryKey, Case.Sensitive);
        logs.Text.ShouldContain("[redacted]");
    }

    [Fact]
    public async Task Every_key_a_provider_reports_is_replaced_not_only_the_first()
    {
        using var logs = new CapturedLogs();
        using var client = Registry(
                logs,
                new ScriptedProvider("keyed", [PrimaryKey, SecondKey], new InvalidOperationException($"refused {PrimaryKey} and {SecondKey}")))
            .CreateChatClient(Binding("keyed"));

        var thrown = await Should.ThrowAsync<ProviderInvocationException>(() => CallAsync(client, streamed: false));

        Written(thrown).ShouldNotContain(PrimaryKey, Case.Sensitive);
        Written(thrown).ShouldNotContain(SecondKey, Case.Sensitive);
        logs.Text.ShouldContain("refused [redacted] and [redacted]");
    }

    [Fact]
    public async Task A_provider_that_reports_no_key_keeps_the_text_of_its_failure_as_it_was()
    {
        // The documented limit: Tracon cannot mask a value it was not told of. The inner exception stays the real
        // one, so a writer keeps the real types of a call with nothing to mask.
        var raw = new InvalidOperationException($"refused {PrimaryKey}");
        using var logs = new CapturedLogs();
        using var client = Registry(logs, new ScriptedProvider("silent", reported: null, raw)).CreateChatClient(Binding("silent"));

        var thrown = await Should.ThrowAsync<ProviderInvocationException>(() => CallAsync(client, streamed: false));

        thrown.InnerException.ShouldBeSameAs(raw);
        Written(thrown).ShouldContain(PrimaryKey, Case.Sensitive);
    }

    [Fact]
    public async Task A_getter_that_throws_neither_breaks_the_build_nor_puts_its_text_in_the_log()
    {
        var raw = new InvalidOperationException($"refused {PrimaryKey}");
        using var logs = new CapturedLogs();
        var provider = new ScriptedProvider("broken", reported: null, raw) { GetterFailure = new InvalidOperationException($"the getter holds {PrimaryKey}") };

        using var client = Should.NotThrow(() => Registry(logs, provider).CreateChatClient(Binding("broken")));

        var thrown = await Should.ThrowAsync<ProviderInvocationException>(() => CallAsync(client, streamed: false));

        thrown.InnerException.ShouldBeSameAs(raw);

        var warning = logs.Records.Where(static record => record.Level == LogLevel.Warning && record.Message.Contains("failed to report", StringComparison.Ordinal)).ShouldHaveSingleItem();

        warning.Message.ShouldContain("broken");
        warning.Message.ShouldContain(typeof(InvalidOperationException).FullName!);
        warning.Message.ShouldNotContain(PrimaryKey, Case.Sensitive);
        warning.ExceptionText.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_fallback_chain_masks_each_link_with_the_keys_of_that_link()
    {
        using var logs = new CapturedLogs();
        var primary = new ScriptedProvider("primary", [PrimaryKey], Unavailable($"The primary refused {PrimaryKey}."));
        var fallback = new ScriptedProvider("fallback", [FallbackKey], Unavailable($"The fallback refused {FallbackKey}."));
        using var client = Registry(logs, primary, fallback).CreateChatClient(Binding("primary", fallback: "fallback"));

        var thrown = await Should.ThrowAsync<TraconProviderUnavailableException>(() => CallAsync(client, streamed: false));

        Written(thrown).ShouldNotContain(PrimaryKey, Case.Sensitive);
        Written(thrown).ShouldNotContain(FallbackKey, Case.Sensitive);
        logs.Text.ShouldNotContain(PrimaryKey, Case.Sensitive);
        logs.Text.ShouldNotContain(FallbackKey, Case.Sensitive);
        logs.Text.ShouldContain("The primary refused [redacted].");
    }

    [Fact]
    public async Task A_key_of_another_provider_is_not_this_links_to_replace()
    {
        // One link holds one set of keys. The text a link writes changes for its own keys and for nobody else's.
        using var logs = new CapturedLogs();
        var primary = new ScriptedProvider("primary", [PrimaryKey], new InvalidOperationException($"refused {PrimaryKey} and {FallbackKey}"));
        var other = new ScriptedProvider("other", [FallbackKey], new InvalidOperationException("unused"));
        using var client = Registry(logs, primary, other).CreateChatClient(Binding("primary"));

        var thrown = await Should.ThrowAsync<ProviderInvocationException>(() => CallAsync(client, streamed: false));

        Written(thrown).ShouldNotContain(PrimaryKey, Case.Sensitive);
        Written(thrown).ShouldContain(FallbackKey, Case.Sensitive);
    }

    [Fact]
    public async Task A_consumers_classifier_sees_the_provider_exception_behind_a_masked_message()
    {
        // The classifier is consumer code: it matches an SDK exception type, and it walks the inner exceptions to find
        // one. It still finds the type, and what it can read as text is masked.
        var raw = new AcmeSdkException($"HTTP 429 quota. Bearer {PrimaryKey}");
        var classifier = new RecordingClassifier();
        using var logs = new CapturedLogs();
        var fallback = new ScriptedProvider("fallback", [FallbackKey], new InvalidOperationException("unused"));
        using var client = Registry(logs, classifier, new ScriptedProvider("primary", [PrimaryKey], raw), fallback)
            .CreateChatClient(Binding("primary", fallback: "fallback"));

        _ = await Should.ThrowAsync<ProviderInvocationException>(() => CallAsync(client, streamed: false));

        // Asked once for each link that failed: the primary, then the fallback link that failed in its turn.
        classifier.Seen.Count.ShouldBe(2);

        var primarySeen = classifier.Seen[0];

        primarySeen.InnerException.ShouldBeSameAs(raw);
        primarySeen.InnerException.ShouldBeOfType<AcmeSdkException>();
        primarySeen.Message.ShouldNotContain(PrimaryKey, Case.Sensitive);
        primarySeen.ToString().ShouldNotContain(PrimaryKey, Case.Sensitive);

        // The failure of a fallback link reaches the classifier as the normalized exception, with its inner exception
        // the real one in the same way.
        classifier.Seen[1].ShouldBeOfType<ProviderInvocationException>().InnerException.ShouldBeOfType<InvalidOperationException>();
    }

    private static ModelProviderRegistry Registry(CapturedLogs logs, params IModelProvider[] providers)
        => Registry(logs, retryClassifier: null, providers);

    private static ModelProviderRegistry Registry(CapturedLogs logs, IProviderRetryClassifier? retryClassifier, params IModelProvider[] providers)
        => new(providers, loggerFactory: logs.Factory, retryClassifier: retryClassifier);

    private static ModelBinding Binding(string provider, string? fallback = null)
        => new()
        {
            Provider = provider,
            Model = "m",
            Fallbacks = fallback is null ? [] : [new ModelFallback { Provider = fallback, Model = "m" }],
        };

    private static HttpRequestException Unavailable(string message)
        => new(message, inner: null, System.Net.HttpStatusCode.ServiceUnavailable);

    private static async Task CallAsync(IChatClient client, bool streamed)
    {
        if (!streamed)
        {
            _ = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

            return;
        }

        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken))
        {
            _ = update;
        }
    }

    // What a writer takes from an exception: the text, and type, message and stack of each inner exception.
    private static string Written(Exception exception)
    {
        var lines = new List<string>();
        var pending = new Stack<Exception>([exception]);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            lines.Add($"{current.GetType().FullName}|{current.Message}|{current.StackTrace}|{current}");

            if (current is AggregateException aggregate)
            {
                foreach (var branch in aggregate.InnerExceptions)
                {
                    pending.Push(branch);
                }
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push(inner);
            }
        }

        return string.Join('\n', lines);
    }

    private sealed class AcmeSdkException(string message) : Exception(message);

    private sealed class RecordingClassifier : IProviderRetryClassifier
    {
        public List<Exception> Seen { get; } = [];

        public ProviderRetryDecision Classify(Exception exception)
        {
            Seen.Add(exception);

            return exception.InnerException is AcmeSdkException ? ProviderRetryDecision.Retry : ProviderRetryDecision.DoNotRetry;
        }
    }

    private sealed class ScriptedProvider(string name, IReadOnlyList<string>? reported, Exception failure) : IModelProvider
    {
        public string Name { get; } = name;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [];

        public Exception? GetterFailure { get; init; }

        public IReadOnlyList<string> SetupApiKeys => GetterFailure is { } getter ? throw getter : reported ?? [];

        public IChatClient CreateChatClient(ModelBinding binding) => new ThrowingClient(failure);
    }

    private sealed class ThrowingClient(Exception failure) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw failure;

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw failure;
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed record LogRecord(LogLevel Level, string Message, string ExceptionText);

    private sealed class CapturedLogs : ILoggerProvider
    {
        private readonly List<LogRecord> _records = [];

        public CapturedLogs()
        {
            Factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(this));
        }

        public ILoggerFactory Factory { get; }

        public IReadOnlyList<LogRecord> Records
        {
            get
            {
                lock (_records)
                {
                    return [.. _records];
                }
            }
        }

        public string Text => string.Join('\n', Records.Select(static record => $"{record.Message}\n{record.ExceptionText}"));

        public ILogger CreateLogger(string categoryName) => new Recorder(_records);

        public void Dispose() => Factory.Dispose();

        private sealed class Recorder(List<LogRecord> records) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var text = exception is null ? string.Empty : Written(exception);

                lock (records)
                {
                    records.Add(new LogRecord(logLevel, formatter(state, exception), text));
                }
            }
        }
    }
}
