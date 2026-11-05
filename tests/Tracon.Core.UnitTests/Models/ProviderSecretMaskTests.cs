using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// The mask that replaces a call's own credential in the text of a provider failure.
/// </summary>
/// <remarks>
/// The mask is for display. These tests pin both halves: what it replaces (the call's value, whole, in the three shapes a
/// body carries it) and what it leaves alone (everything else, including a provider's own partial quote and another
/// call's value). The decisions that read the exception graph (retry, circuit, status) must come out the same for the
/// masked exception as for the raw one.
/// </remarks>
public sealed class ProviderSecretMaskTests
{
    // Not a pattern a secret scanner knows: the literal is a test value and has to stay one.
    private const string Value = "canary-mask-unit-value-7f3a91c2";
    private const string OtherValue = "canary-mask-other-tenant-5d0b44e8";

    [Fact]
    public void The_call_value_is_replaced_and_the_rest_of_the_text_stays()
    {
        var mask = ProviderSecretMask.ForValue(Value);

        mask.Apply($"Incorrect API key provided: Bearer {Value}. Find your key at the console.")
            .ShouldBe("Incorrect API key provided: Bearer [redacted]. Find your key at the console.");
    }

    [Fact]
    public void Every_occurrence_is_replaced()
    {
        var mask = ProviderSecretMask.ForValue(Value);

        mask.Apply($"{Value} and again {Value}\n{Value}").ShouldBe("[redacted] and again [redacted]\n[redacted]");
    }

    [Fact]
    public void A_value_that_is_not_valid_text_still_builds_a_mask_and_the_raw_value_is_replaced()
    {
        // An unpaired surrogate cannot be escaped for JSON or a URL. Building the mask runs while a client is built,
        // outside any handler of a provider failure, so it must not throw for a value the encoders refuse.
        var value = "canary-unpaired-surrogate-\uD800-value";

        var mask = Should.NotThrow(() => ProviderSecretMask.ForValue(value));

        mask.Apply($"key: {value} end").ShouldBe("key: [redacted] end");
    }

    [Theory]
    [InlineData("kd8+Q2/xv0Zp9LmN3wRt5YbE7UaHcJfG")]
    [InlineData("quoted\"and\\slashed-key-value-9981")]
    public void The_json_escaped_and_url_encoded_shapes_are_replaced(string value)
    {
        var mask = ProviderSecretMask.ForValue(value);
        var shapes = new[]
        {
            value,
            System.Text.Json.JsonEncodedText.Encode(value).Value,
            System.Text.Json.JsonEncodedText.Encode(value, System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping).Value,
            System.Text.Json.JsonEncodedText.Encode(value, System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping).Value.Replace("/", "\\/", StringComparison.Ordinal),
            Uri.EscapeDataString(value),
            WebUtility.UrlEncode(value),
        };

        foreach (var shape in shapes)
        {
            mask.Apply($"body: {shape} end").ShouldBe("body: [redacted] end", $"the shape '{shape}' was not replaced");
        }
    }

    [Fact]
    public void A_partial_quote_a_provider_makes_of_the_key_is_left_as_it_is()
    {
        // OpenAI quotes the first eight and the last four characters of a key it refuses, asterisks between. The match
        // is exact, not a pattern of keys: that quote is the provider's own and it stays.
        var mask = ProviderSecretMask.ForValue("kx-test-abcdefghijklmnopqrstuvwxyz0123456789ABCD");
        const string quote = "Incorrect API key provided: kx-test-*****************************ABCD.";

        mask.Apply(quote).ShouldBe(quote);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("1234567")]
    public void A_missing_or_short_value_masks_nothing(string? value)
    {
        var mask = ProviderSecretMask.ForValue(value);
        var text = $"the provider said {value} and ordinary words";

        mask.Apply(text).ShouldBeSameAs(text);
    }

    [Fact]
    public void A_value_at_the_minimum_length_is_masked()
    {
        var value = new string('k', ProviderSecretMask.MinimumValueLength);

        ProviderSecretMask.ForValue(value).Apply($"a {value} b").ShouldBe("a [redacted] b");
    }

    [Fact]
    public void A_call_without_a_credential_has_the_empty_mask()
    {
        ProviderSecretMask.For(credential: null).ShouldBeSameAs(ProviderSecretMask.Empty);
        ProviderSecretMask.For(new ModelProviderCredential { ApiKey = Value }).Apply($"x {Value}").ShouldBe("x [redacted]");
    }

    [Fact]
    public void Another_calls_value_is_not_this_masks_to_change()
    {
        // A mask holds the value of ONE call. A text that quotes another tenant's value is left whole: no mask carries
        // a set of every tenant's keys, so no call can change another call's text.
        var mask = ProviderSecretMask.ForValue(Value);
        var text = $"the provider quoted {OtherValue}";

        mask.Apply(text).ShouldBe(text);
    }

    [Fact]
    public void A_text_of_a_megabyte_and_more_is_masked_in_every_place()
    {
        var mask = ProviderSecretMask.ForValue(Value);
        var chunk = new string('x', 1024 * 128);
        var text = $"{chunk}{Value}{chunk}{Value}{chunk}{Value}{chunk}{Value}{chunk}{Value}{chunk}{Value}{chunk}{Value}{chunk}{Value}{chunk}";

        var masked = mask.Apply(text);

        masked.ShouldNotContain(Value, Case.Sensitive);
        masked.Split("[redacted]").Length.ShouldBe(9);
        masked.Length.ShouldBe((chunk.Length * 9) + (8 * "[redacted]".Length));
    }

    [Fact]
    public void Concurrent_use_of_one_mask_gives_the_same_answer()
    {
        var mask = ProviderSecretMask.ForValue(Value);
        var text = $"left {Value} right";

        var results = Enumerable.Range(0, 64)
            .AsParallel()
            .WithDegreeOfParallelism(8)
            .Select(_ => mask.Apply(text))
            .ToList();

        results.ShouldAllBe(result => string.Equals(result, "left [redacted] right", StringComparison.Ordinal));
    }

    [Fact]
    public void Concurrent_use_of_one_mask_of_several_values_gives_the_same_answer()
    {
        const string other = "canary-mask-second-setup-key-2e6c8b10";
        var mask = ProviderSecretMask.ForValues([Value, other]);
        var text = $"left {Value} middle {other} right";

        var results = Enumerable.Range(0, 64)
            .AsParallel()
            .WithDegreeOfParallelism(8)
            .Select(_ => mask.Apply(text))
            .ToList();

        results.ShouldAllBe(result => string.Equals(result, "left [redacted] middle [redacted] right", StringComparison.Ordinal));
    }

    [Fact]
    public void An_exception_whose_text_cannot_be_read_is_withheld_and_never_shown_raw()
    {
        var mask = ProviderSecretMask.ForValue(Value);
        var hostile = new HostileException($"quoted {Value}");

        var view = mask.View(hostile);

        view.ToString().ShouldNotContain(Value, Case.Sensitive);
        view.ToString().ShouldContain(nameof(HostileException));
        view.ToString().ShouldContain("withheld");
        mask.TextOf(hostile).ShouldNotContain(Value, Case.Sensitive);
    }

    [Fact]
    public void A_wrapper_around_an_exception_whose_text_cannot_be_read_does_not_fail_and_does_not_show_it_raw()
    {
        var mask = ProviderSecretMask.ForValue(Value);

        // The text cannot be produced, the message can: the decisions read the message, the loggers read the text.
        var wrapper = new ForeignProviderInvocationException(new HostileTextException($"quoted {Value}"), mask);
        var invocation = ProviderInvocationException.UpstreamFailure(new HostileTextException($"quoted {Value}"), "openai", mask);

        wrapper.ToString().ShouldNotContain(Value, Case.Sensitive);
        wrapper.ToString().ShouldContain("withheld");
        wrapper.Message.ShouldNotContain(Value, Case.Sensitive);
        invocation.ToString().ShouldNotContain(Value, Case.Sensitive);
        invocation.ToString().ShouldContain("withheld");
    }

    [Fact]
    public void The_view_keeps_type_names_messages_and_stack_and_has_no_graph_behind_it()
    {
        var mask = ProviderSecretMask.ForValue(Value);
        var inner = new InvalidOperationException($"HTTP 401 Incorrect API key provided: Bearer {Value}");
        var outer = ThrownWith(new AggregateException("Retry failed after 4 tries.", inner));

        var view = mask.View(outer);

        view.InnerException.ShouldBeNull();
        view.Message.ShouldContain("Retry failed after 4 tries.");
        view.ToString().ShouldNotContain(Value, Case.Sensitive);
        view.ToString().ShouldContain(nameof(AggregateException));
        view.ToString().ShouldContain(nameof(InvalidOperationException));
        view.ToString().ShouldContain("HTTP 401 Incorrect API key provided: Bearer [redacted]");
        view.ToString().ShouldContain(nameof(ThrownWith), Case.Sensitive);
        ((MaskedFailureView)view).OriginalTypeName.ShouldBe(typeof(AggregateException).FullName);
    }

    [Fact]
    public void The_tagged_exception_says_nothing_of_the_value_and_still_holds_the_raw_failure_for_decisions()
    {
        var mask = ProviderSecretMask.ForValue(Value);
        var raw = new HttpRequestException($"HTTP 401 Incorrect API key provided: Bearer {Value}", inner: null, HttpStatusCode.Unauthorized);

        var tagged = new ForeignProviderInvocationException(raw, mask);

        tagged.Message.ShouldNotContain(Value, Case.Sensitive);
        tagged.Message.ShouldContain("HTTP 401 Incorrect API key provided: Bearer [redacted]");
        tagged.ToString().ShouldNotContain(Value, Case.Sensitive);
        tagged.ToString().ShouldContain(nameof(ForeignProviderInvocationException));
        tagged.ToString().ShouldContain(nameof(HttpRequestException));

        // The decisions read the raw graph through Original. What a writer reaches through the inner exception is the
        // masked view of it.
        tagged.Original.ShouldBeSameAs(raw);
        tagged.InnerException.ShouldBeOfType<MaskedFailureView>();
        tagged.InnerException.InnerException.ShouldBeNull();
        tagged.InnerException.Message.ShouldNotContain(Value, Case.Sensitive);
        tagged.InnerException.ToString().ShouldNotContain(Value, Case.Sensitive);
    }

    [Fact]
    public void A_call_with_nothing_to_mask_keeps_the_real_exception_as_the_inner_exception()
    {
        // No value to hide, so a writer that walks the graph keeps seeing the real types.
        var raw = new HttpRequestException("HTTP 503 unavailable", inner: null, HttpStatusCode.ServiceUnavailable);

        new ForeignProviderInvocationException(raw, ProviderSecretMask.Empty).InnerException.ShouldBeSameAs(raw);
        ProviderInvocationException.UpstreamFailure(raw, "openai", ProviderSecretMask.Empty).InnerException.ShouldBeSameAs(raw);
        ProviderSecretMask.Empty.Shown(raw).ShouldBeSameAs(raw);
    }

    [Fact]
    public void A_writer_that_walks_the_graph_property_by_property_reaches_no_raw_frame()
    {
        // The shape of an error tracker or a structured exception formatter: type, message, stack, data and every inner
        // exception, aggregate branches included. None of it may carry the value, for either exception Tracon throws.
        var mask = ProviderSecretMask.ForValue(Value);
        var inner = ThrownWith(new InvalidOperationException($"HTTP 401 Incorrect API key provided: Bearer {Value}"));
        inner.Data["quoted"] = Value;

        var raw = ThrownWith(new AggregateException($"Retry failed. Bearer {Value}", inner));

        var tagged = new ForeignProviderInvocationException(raw, mask);
        var normalized = ProviderInvocationException.UpstreamFailure(raw, "openai", mask);

        foreach (var written in new[] { Write(tagged), Write(normalized) })
        {
            written.ShouldNotContain(Value, Case.Sensitive);
            written.ShouldContain("[redacted]");
        }
    }

    [Fact]
    public void A_stack_trace_and_data_of_the_view_carry_no_value()
    {
        var mask = ProviderSecretMask.ForValue(Value);
        var raw = ThrownWith(new InvalidOperationException($"Bearer {Value}"));
        raw.Data["key"] = Value;

        var view = new ForeignProviderInvocationException(raw, mask).InnerException!;

        view.StackTrace.ShouldBeNull();
        view.Data.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void Every_decision_reads_the_same_answer_from_the_masked_exception_as_from_the_raw_one(HttpStatusCode status)
    {
        var mask = ProviderSecretMask.ForValue(Value);
        var raw = new HttpRequestException($"Response status code does not indicate success: {(int)status}. Bearer {Value}", inner: null, status);
        var tagged = new ForeignProviderInvocationException(raw, mask);
        var normalized = ProviderInvocationException.UpstreamFailure(raw, "openai", mask);

        ProviderHttpStatus.Read(tagged).ShouldBe(ProviderHttpStatus.Read(raw));
        ProviderHttpStatus.IsPermanentFailure(tagged).ShouldBe(ProviderHttpStatus.IsPermanentFailure(raw));
        FallbackRetryClassifier.Classify(tagged).ShouldBe(FallbackRetryClassifier.Classify(raw));
        FallbackRetryClassifier.IsRetryable(tagged).ShouldBe(FallbackRetryClassifier.IsRetryable(raw));
        CircuitBreakingChatClient.CountsAgainstProvider(tagged).ShouldBe(CircuitBreakingChatClient.CountsAgainstProvider(new ForeignProviderInvocationException(raw, ProviderSecretMask.Empty)));

        // After the normalizer the decisions read what the normalized exception carries.
        normalized.StatusCode.ShouldBe((int)status);
        FallbackRetryClassifier.IsRetryable(normalized).ShouldBe(FallbackRetryClassifier.IsRetryable(raw));
        FinalFailure.IsFinal(normalized).ShouldBe(ProviderHttpStatus.IsPermanent((int)status));
    }

    [Fact]
    public void A_connection_failure_without_a_status_is_still_a_transport_error_through_the_masked_exception()
    {
        var mask = ProviderSecretMask.ForValue(Value);
        var raw = new HttpRequestException($"Connection refused to host with {Value}", new System.Net.Sockets.SocketException(10061));

        FallbackRetryClassifier.Classify(new ForeignProviderInvocationException(raw, mask))
            .ShouldBe(FallbackRetryClassifier.Classify(raw));
        FallbackRetryClassifier.IsRetryable(ProviderInvocationException.UpstreamFailure(raw, "openai", mask))
            .ShouldBe(FallbackRetryClassifier.IsRetryable(raw));
    }

    [Fact]
    public void The_normalized_exception_writes_the_provider_text_without_the_value_and_keeps_the_raw_failure_inside()
    {
        var mask = ProviderSecretMask.ForValue(Value);
        var raw = ThrownWith(new InvalidOperationException($"HTTP 401 Incorrect API key provided: Bearer {Value}"));

        var normalized = ProviderInvocationException.UpstreamFailure(raw, "openai", mask);

        normalized.Message.ShouldNotContain(Value, Case.Sensitive);
        normalized.ToString().ShouldNotContain(Value, Case.Sensitive);
        normalized.ToString().ShouldContain("Incorrect API key provided: Bearer [redacted]");
        normalized.ToString().ShouldContain(nameof(ProviderInvocationException));
        normalized.Original.ShouldBeSameAs(raw);
        normalized.InnerException.ShouldBeOfType<MaskedFailureView>();
        normalized.InnerException.ToString().ShouldNotContain(Value, Case.Sensitive);
    }

    [Fact]
    public void Several_values_are_masked_together_and_a_missing_or_short_one_is_skipped()
    {
        const string other = "canary-mask-second-setup-key-2e6c8b10";

        var mask = ProviderSecretMask.ForValues([Value, null, "short", other]);

        mask.Apply($"first {Value}, second {other}, third short").ShouldBe("first [redacted], second [redacted], third short");
    }

    [Fact]
    public void A_value_that_contains_another_is_replaced_whole_whichever_order_they_are_given()
    {
        var longer = Value + "-with-a-tail";

        foreach (var values in new[] { new[] { Value, longer }, [longer, Value] })
        {
            ProviderSecretMask.ForValues(values).Apply($"a {longer} b").ShouldBe("a [redacted] b");
        }
    }

    [Fact]
    public void No_value_long_enough_gives_the_empty_mask()
    {
        ProviderSecretMask.ForValues([]).ShouldBeSameAs(ProviderSecretMask.Empty);
        ProviderSecretMask.ForValues([null, "short"]).ShouldBeSameAs(ProviderSecretMask.Empty);
    }

    [Fact]
    public async Task The_tagging_client_hands_the_masked_exception_to_the_ring_above_it()
    {
        var mask = ProviderSecretMask.ForValue(Value);
        using var client = new ProviderFailureTaggingChatClient(
            new ThrowingChatClient(new InvalidOperationException($"Incorrect API key provided: Bearer {Value}")),
            mask);

        var thrown = await Should.ThrowAsync<ForeignProviderInvocationException>(() => client.GetResponseAsync([]));

        thrown.Message.ShouldBe("Incorrect API key provided: Bearer [redacted]");
        thrown.ToString().ShouldNotContain(Value, Case.Sensitive);

        var streamed = await Should.ThrowAsync<ForeignProviderInvocationException>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync([]))
            {
                _ = update;
            }
        });

        streamed.Message.ShouldBe("Incorrect API key provided: Bearer [redacted]");
        streamed.ToString().ShouldNotContain(Value, Case.Sensitive);
    }

    [Fact]
    public void The_model_provider_log_gets_the_masked_view_when_the_call_had_a_credential_and_the_exception_itself_when_it_had_none()
    {
        var original = new InvalidOperationException($"HTTP 401 Incorrect API key provided: Bearer {Value}");
        var binding = new ModelBinding { Provider = "openai", Model = "gpt-test" };
        var recorded = new RecordingExceptionLogger();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(recorded));

        ProviderFailureNormalizer.Log(factory, binding, original, "invocation", ProviderSecretMask.ForValue(Value));
        ProviderFailureNormalizer.Log(factory, binding, original, "invocation", ProviderSecretMask.Empty);

        recorded.Exceptions.Count.ShouldBe(2);

        var masked = recorded.Exceptions[0];

        masked.ShouldNotBeSameAs(original);
        masked.ShouldBeOfType<MaskedFailureView>();
        masked.ToString().ShouldNotContain(Value, Case.Sensitive);
        masked.ToString().ShouldContain("Incorrect API key provided: Bearer [redacted]");
        masked.InnerException.ShouldBeNull();
        recorded.Exceptions[1].ShouldBeSameAs(original);
    }

    [Fact]
    public void A_cancellation_is_not_a_provider_failure_and_is_not_wrapped()
    {
        ProviderFailureNormalizer.ShouldNormalize(new OperationCanceledException($"cancelled {Value}")).ShouldBeFalse();
    }

    // What a writer that serializes the graph property by property sends.
    private static string Write(Exception exception)
    {
        var lines = new List<string>();
        var pending = new Stack<Exception>([exception]);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            lines.Add($"type={current.GetType().FullName} message={current.Message} stack={current.StackTrace} text={current}");
            lines.AddRange(current.Data.Cast<System.Collections.DictionaryEntry>().Select(static entry => $"data {entry.Key}={entry.Value}"));

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

    private static Exception ThrownWith(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception thrown)
        {
            return thrown;
        }
    }

    /// <summary>An exception whose own text cannot be produced, as a hostile or broken SDK type can be.</summary>
    private sealed class HostileException(string message) : Exception(message)
    {
        public override string Message => throw new InvalidOperationException("the message cannot be read");

        public override string ToString() => throw new InvalidOperationException("the text cannot be read");
    }

    /// <summary>An exception that reads its message and cannot write its full text.</summary>
    private sealed class HostileTextException(string message) : Exception(message)
    {
        public override string ToString() => throw new InvalidOperationException("the text cannot be read");
    }

    private sealed class RecordingExceptionLogger : ILoggerProvider
    {
        private readonly List<Exception> _exceptions = [];

        public IReadOnlyList<Exception> Exceptions
        {
            get
            {
                lock (_exceptions)
                {
                    return [.. _exceptions];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Recorder(_exceptions);

        public void Dispose()
        {
        }

        private sealed class Recorder(List<Exception> exceptions) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (exception is not null)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(exception);
                    }
                }
            }
        }
    }

    private sealed class ThrowingChatClient(Exception failure) : IChatClient
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
}
