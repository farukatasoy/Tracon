using System.Globalization;
using System.Net;
using Shouldly;
using Tracon.Voice.UnitTests.Infrastructure;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// A speech or dialogue request that fails in a way a repeat can change is sent again, up to <see cref="VoiceOptions.MaxRetries"/>
/// times, while it still holds its place among the client's concurrent requests. By default nothing is repeated.
/// </summary>
/// <remarks>
/// <para>
/// A batch that voices a podcast is five requests that cost money each; one that drops must not make the host pay for the four that
/// finished by starting over, and a host that writes its own loop around the client cannot keep the pool's place while it waits.
/// The cases hold what is repeated (<c>429</c>, <c>5xx</c>, a connection that failed), what is not (any other <c>4xx</c>, the client's
/// own timeout, a cancellation, streaming, transcription), how long the wait is, and that the wait runs on the host's clock, so no
/// case waits for real time.
/// </para>
/// <para>
/// The unit is the client over a fake <see cref="HttpMessageHandler"/>. What reaches the provider is read from the requests the
/// handler recorded; what the wait was is read from the steps the fake clock was moved by.
/// </para>
/// </remarks>
public sealed class ElevenLabsRetryTests
{
    private static readonly byte[] Mp3Bytes = DialogueTestData.Mp3Bytes;

    private static readonly TimeSpan FirstBackoff = TimeSpan.FromMilliseconds(800);

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Without_the_setting_a_failed_request_reaches_the_provider_once(HttpStatusCode status)
    {
        var clock = new ManualClock();
        var handler = Answers(_ => Failure(status));
        using var client = Create(handler, clock, maxRetries: 0);

        var failure = await Should.ThrowAsync<ProviderInvocationException>(
            async () => await client.SynthesizeAsync(Speech("hello"), TestContext.Current.CancellationToken));

        failure.StatusCode.ShouldBe((int)status);
        handler.Requests.Count.ShouldBe(1);
        clock.CreatedTimers.ShouldBe(0, "no wait is armed when nothing is repeated");
    }

    [Fact]
    public async Task A_503_that_clears_is_repeated_until_it_succeeds_and_waits_the_backoff_between_the_tries()
    {
        var clock = new ManualClock();
        var handler = Answers(_ => Failure(HttpStatusCode.ServiceUnavailable), _ => Failure(HttpStatusCode.ServiceUnavailable), _ => Success());
        using var client = Create(handler, clock, maxRetries: 2);

        var audio = await Run(clock, client.SynthesizeAsync(Speech("hello"), TestContext.Current.CancellationToken));

        audio.Data.ToArray().ShouldBe(Mp3Bytes);
        handler.Requests.Count.ShouldBe(3);
        clock.Steps.ShouldBe([FirstBackoff, FirstBackoff * 2], "0.8 seconds, doubled for the second repeat");

        // 🚨 A request message cannot be sent twice: every try builds its own, with the same content.
        handler.Requests.Select(static request => request.Body).Distinct(StringComparer.Ordinal).Count().ShouldBe(1);
        handler.Requests.ShouldAllBe(static request => request.Headers.ContainsKey("xi-api-key"));
    }

    [Fact]
    public async Task A_request_that_succeeds_is_sent_once_whatever_the_setting()
    {
        var clock = new ManualClock();
        var handler = Answers(_ => Success());
        using var client = Create(handler, clock, maxRetries: 5);

        _ = await client.SynthesizeAsync(Speech("hello"), TestContext.Current.CancellationToken);

        handler.Requests.Count.ShouldBe(1);
        clock.CreatedTimers.ShouldBe(0);
    }

    [Fact]
    public async Task When_every_repeat_fails_the_last_failure_is_thrown_after_one_try_and_the_configured_repeats()
    {
        var clock = new ManualClock();
        var handler = Answers(_ => Failure(HttpStatusCode.ServiceUnavailable));
        using var client = Create(handler, clock, maxRetries: 2);

        var failure = await Should.ThrowAsync<ProviderInvocationException>(
            async () => await Run(clock, client.SynthesizeAsync(Speech("hello"), TestContext.Current.CancellationToken)));

        handler.Requests.Count.ShouldBe(3);
        failure.StatusCode.ShouldBe(503);
        failure.IsTransient.ShouldBeTrue();
        failure.Message.ShouldBe("Speech could not be generated: HTTP 503.", "the failure is the one a single try gives");
        failure.ProviderErrorCode.ShouldBe("busy");
    }

    [Theory]
    [InlineData("3", 3000)]
    [InlineData("60", 60000)]
    [InlineData("1", 1000)]
    public async Task A_429_waits_for_the_Retry_After_the_provider_asked_for(string header, int milliseconds)
    {
        var clock = new ManualClock();
        var handler = Answers(_ => Failure(HttpStatusCode.TooManyRequests, retryAfter: header), _ => Success());
        using var client = Create(handler, clock, maxRetries: 1);

        _ = await Run(clock, client.SynthesizeAsync(Speech("hello"), TestContext.Current.CancellationToken));

        clock.Steps.ShouldBe([TimeSpan.FromMilliseconds(milliseconds)]);
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_Retry_After_that_is_a_date_is_the_wait_from_now()
    {
        var clock = new ManualClock();
        var date = clock.GetUtcNow().AddSeconds(5).ToString("R", CultureInfo.InvariantCulture);
        var handler = Answers(_ => Failure(HttpStatusCode.ServiceUnavailable, retryAfter: date), _ => Success());
        using var client = Create(handler, clock, maxRetries: 1);

        _ = await Run(clock, client.SynthesizeAsync(Speech("hello"), TestContext.Current.CancellationToken));

        clock.Steps.ShouldBe([TimeSpan.FromSeconds(5)]);
    }

    [Theory]
    [InlineData("61")]
    [InlineData("86400")]
    [InlineData("99999999999999")]
    [InlineData("soon")]
    [InlineData("-5")]
    public async Task A_Retry_After_over_a_minute_or_not_a_delay_is_ignored_and_the_backoff_is_used(string header)
    {
        var clock = new ManualClock();
        var handler = Answers(_ => Failure(HttpStatusCode.TooManyRequests, retryAfter: header), _ => Success());
        using var client = Create(handler, clock, maxRetries: 1);

        _ = await Run(clock, client.SynthesizeAsync(Speech("hello"), TestContext.Current.CancellationToken));

        clock.Steps.ShouldBe([FirstBackoff], "a provider that asks for a day does not hold a slot for a day");
    }

    [Fact]
    public async Task A_Retry_After_of_zero_repeats_at_once()
    {
        var clock = new ManualClock();
        var handler = Answers(_ => Failure(HttpStatusCode.TooManyRequests, retryAfter: "0"), _ => Success());
        using var client = Create(handler, clock, maxRetries: 1);

        _ = await client.SynthesizeAsync(Speech("hello"), TestContext.Current.CancellationToken);

        handler.Requests.Count.ShouldBe(2);
        clock.Steps.ShouldBeEmpty("a wait of nothing arms no timer");
    }

    [Fact]
    public async Task The_backoff_doubles_for_every_repeat_and_stops_at_a_minute()
    {
        var clock = new ManualClock();
        var handler = Answers(_ => Failure(HttpStatusCode.BadGateway));
        using var client = Create(handler, clock, maxRetries: 10);

        await Should.ThrowAsync<ProviderInvocationException>(
            async () => await Run(clock, client.SynthesizeAsync(Speech("hello"), TestContext.Current.CancellationToken)));

        handler.Requests.Count.ShouldBe(11);
        clock.Steps.Select(static step => (int)step.TotalMilliseconds).ShouldBe(
            [800, 1600, 3200, 6400, 12800, 25600, 51200, 60000, 60000, 60000]);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task A_failure_a_repeat_cannot_change_is_sent_once(HttpStatusCode status)
    {
        var clock = new ManualClock();
        var handler = Answers(_ => Failure(status));
        using var client = Create(handler, clock, maxRetries: 3);

        var failure = await Should.ThrowAsync<ProviderInvocationException>(
            async () => await client.SynthesizeAsync(Speech("hello"), TestContext.Current.CancellationToken));

        failure.StatusCode.ShouldBe((int)status);
        handler.Requests.Count.ShouldBe(1);
        clock.CreatedTimers.ShouldBe(0);
    }

    [Fact]
    public async Task The_clients_own_timeout_is_not_repeated_because_the_provider_may_have_finished_and_billed_the_request()
    {
        var clock = new ManualClock();

        // HttpClient reports its own deadline as a cancellation the caller did not ask for.
        var handler = new StubHttpMessageHandler(static _ => throw new TaskCanceledException());
        using var client = Create(handler, clock, maxRetries: 3);

        var failure = await Should.ThrowAsync<ProviderInvocationException>(
            async () => await client.SynthesizeAsync(Speech("hello"), TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("timed out", Case.Sensitive);
        handler.Requests.Count.ShouldBe(1);
        clock.CreatedTimers.ShouldBe(0);
    }

    [Fact]
    public async Task A_connection_that_failed_before_an_answer_is_repeated()
    {
        var clock = new ManualClock();
        var calls = 0;
        var handler = new StubHttpMessageHandler(_ => Interlocked.Increment(ref calls) == 1
            ? throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (secret-host.internal:443)")
            : Success());
        using var client = Create(handler, clock, maxRetries: 1);

        var audio = await Run(clock, client.SynthesizeAsync(Speech("hello"), TestContext.Current.CancellationToken));

        audio.Data.Length.ShouldBeGreaterThan(0);
        handler.Requests.Count.ShouldBe(2);
        clock.Steps.ShouldBe([FirstBackoff]);
    }

    [Fact]
    public async Task A_connection_that_dropped_while_the_answer_was_read_is_repeated_like_one_that_failed_before_it()
    {
        // The answer is read to the end inside the send, so the drop reaches the client as the same transport failure.
        var clock = new ManualClock();
        var calls = 0;
        var handler = new StubHttpMessageHandler(_ => Interlocked.Increment(ref calls) == 1
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new DroppedContent() }
            : Success());
        using var client = Create(handler, clock, maxRetries: 1);

        var audio = await Run(clock, client.SynthesizeAsync(Speech("hello"), TestContext.Current.CancellationToken));

        audio.Data.ToArray().ShouldBe(Mp3Bytes);
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_connection_that_keeps_failing_ends_as_the_failure_a_single_try_gives_with_no_address()
    {
        var clock = new ManualClock();
        var handler = new StubHttpMessageHandler(
            static _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (secret-host.internal:443)"));
        using var client = Create(handler, clock, maxRetries: 2);

        var failure = await Should.ThrowAsync<ProviderInvocationException>(
            async () => await Run(clock, client.SynthesizeAsync(Speech("hello"), TestContext.Current.CancellationToken)));

        handler.Requests.Count.ShouldBe(3);
        failure.StatusCode.ShouldBeNull();
        failure.IsTransient.ShouldBeTrue();
        failure.Message.ShouldContain("Connection error (ConnectionError)", Case.Sensitive);
        failure.Message.ShouldNotContain("secret-host", Case.Sensitive);
    }

    [Fact]
    public async Task A_cancellation_during_the_wait_ends_the_call_without_another_try_and_gives_the_slot_back()
    {
        var clock = new ManualClock();
        var handler = Answers(_ => Failure(HttpStatusCode.ServiceUnavailable), _ => Success());
        using var client = Create(handler, clock, maxRetries: 3, static options => options.MaxConcurrentRequests = 1);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var call = client.SynthesizeAsync(Speech("hello"), cancellation.Token).AsTask();

        await WaitUntil.TrueAsync(() => clock.Pending.Count == 1, "the call waits to repeat the request");
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await call);

        handler.Requests.Count.ShouldBe(1, "a cancellation is not a try");

        // With a limit of one, a slot kept by the cancelled call would hold this one for ever.
        var next = await client.SynthesizeAsync(Speech("again"), TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        next.Data.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_cancellation_while_a_request_is_in_flight_ends_the_call_and_is_not_counted_as_a_try()
    {
        var clock = new ManualClock();
        var handler = new BlockedHandler();
        using var client = Create(handler, clock, maxRetries: 3, static options => options.MaxConcurrentRequests = 1);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var call = client.SynthesizeAsync(Speech("hello"), cancellation.Token).AsTask();

        await WaitUntil.TrueAsync(() => handler.Entered == 1, "the request reached the provider and is held there");
        await cancellation.CancelAsync();
        await WaitUntil.TrueAsync(() => call.IsCompleted, "the call ends when its caller cancels");

        await Should.ThrowAsync<OperationCanceledException>(async () => await call);

        handler.Entered.ShouldBe(1, "a cancellation is not a connection failure, so nothing is sent again");
        handler.ObservedCancellation.ShouldBeTrue("the caller's token reached the provider call");
        clock.CreatedTimers.ShouldBe(0, "no wait is armed for a repeat that never happens");
    }

    [Fact]
    public async Task A_timestamped_synthesis_is_repeated()
    {
        var clock = new ManualClock();
        var body = $$"""{"audio_base64":"{{Convert.ToBase64String(Mp3Bytes)}}"}""";
        var handler = Answers(_ => Failure(HttpStatusCode.InternalServerError), _ => StubHttpMessageHandler.Json(body));
        using var client = Create(handler, clock, maxRetries: 1);

        var audio = await Run(clock, client.SynthesizeAsync(Speech("hello", timestamps: true), TestContext.Current.CancellationToken));

        audio.Data.ToArray().ShouldBe(Mp3Bytes);
        handler.Requests.Count.ShouldBe(2);
        handler.Requests.ShouldAllBe(static request => request.Uri.AbsolutePath.EndsWith("/with-timestamps", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_dialogue_is_repeated_after_a_5xx_and_the_billed_characters_are_those_of_the_answer_that_succeeded()
    {
        var clock = new ManualClock();
        var handler = Answers(
            _ => Failure(HttpStatusCode.BadGateway, characterCost: "99"),
            _ => DialogueTestData.RawResponse(characterCost: "20"));
        using var client = Create(handler, clock, maxRetries: 1);

        var dialogue = await Run(clock, client.SynthesizeDialogueAsync(DialogueTestData.TwoTurns(), TestContext.Current.CancellationToken));

        handler.Requests.Count.ShouldBe(2);
        dialogue.Audio.CharactersBilled.ShouldBe(20, "the failed try's header is not counted");
        dialogue.Audio.UsageSource.ShouldBe(SpeechUsageSource.Provider);
    }

    [Fact]
    public async Task Streaming_synthesis_is_not_repeated_whatever_the_setting()
    {
        var clock = new ManualClock();
        var handler = Answers(_ => Failure(HttpStatusCode.ServiceUnavailable), _ => Success());
        using var client = Create(handler, clock, maxRetries: 3);

        var failure = await Should.ThrowAsync<ProviderInvocationException>(
            async () =>
            {
                await foreach (var _ in client.SynthesizeStreamingAsync(Speech("hello"), TestContext.Current.CancellationToken))
                {
                    // Nothing arrives: the answer is an error.
                }
            });

        failure.StatusCode.ShouldBe(503);
        handler.Requests.Count.ShouldBe(1, "a conversation's audio cannot wait for a repeat");
        clock.CreatedTimers.ShouldBe(0);
    }

    [Fact]
    public async Task Transcription_is_not_repeated_whatever_the_setting()
    {
        var clock = new ManualClock();
        var handler = Answers(_ => Failure(HttpStatusCode.ServiceUnavailable), _ => StubHttpMessageHandler.Json("""{"text":"hi"}"""));
        using var client = Create(handler, clock, maxRetries: 3);
        using var audio = new MemoryStream(Mp3Bytes);

        var failure = await Should.ThrowAsync<ProviderInvocationException>(
            async () => await client.TranscribeAsync(audio, "audio/mpeg", options: null, TestContext.Current.CancellationToken));

        failure.StatusCode.ShouldBe(503);
        handler.Requests.Count.ShouldBe(1, "the audio of a transcription is a stream that cannot be read again");
        clock.CreatedTimers.ShouldBe(0);
    }

    [Fact]
    public async Task Every_try_builds_its_request_again_with_the_key_of_that_moment()
    {
        var clock = new ManualClock();
        var handler = Answers(_ => Failure(HttpStatusCode.ServiceUnavailable), _ => Success());
        var keys = new Queue<string>(["key-before", "key-after"]);

        using var client = new ElevenLabsSpeechClient(
            new VoiceOptions { DefaultVoiceId = "voice-1", MaxRetries = 1 },
            new HttpClient(handler),
            target: () => new ElevenLabsSpeechClient.ElevenLabsTarget(keys.Dequeue(), ElevenLabsSpeechClient.DefaultEndpoint),
            timeProvider: clock);

        _ = await Run(clock, client.SynthesizeAsync(Speech("hello"), TestContext.Current.CancellationToken));

        handler.Requests.Select(static request => request.Headers["xi-api-key"]).ShouldBe(["key-before", "key-after"], "a key rotated during the wait is the key of the repeat");
    }

    [Fact]
    public async Task A_call_that_repeats_keeps_its_slot_so_a_second_call_waits_for_the_whole_of_it()
    {
        var clock = new ManualClock();
        var handler = Answers(_ => Failure(HttpStatusCode.ServiceUnavailable), _ => Success(), _ => Success());
        using var client = Create(handler, clock, maxRetries: 1, static options => options.MaxConcurrentRequests = 1);

        var first = client.SynthesizeAsync(Speech("first"), TestContext.Current.CancellationToken).AsTask();

        await WaitUntil.TrueAsync(() => clock.Pending.Count == 1, "the first call waits to repeat its request");

        var second = client.SynthesizeAsync(Speech("second"), TestContext.Current.CancellationToken).AsTask();

        // A free slot lets the call run on this thread to the provider, and the handler would have recorded it by now: the second
        // call is parked on the pool, which the first still holds while it waits.
        handler.Requests.Count.ShouldBe(1, "the second call reached the provider while the first waited to repeat");

        await Run(clock, Task.WhenAll(first, second));

        handler.Requests.Select(static request => request.Body!.Contains("\"text\":\"first\"", StringComparison.Ordinal)).ShouldBe([true, true, false]);
        handler.Requests.Count.ShouldBe(3);
    }

    private static async Task<T> Run<T>(ManualClock clock, ValueTask<T> call)
    {
        var work = call.AsTask();

        await clock.AdvanceUntilAsync(work);

        return await work;
    }

    private static async Task Run(ManualClock clock, Task work)
        => await clock.AdvanceUntilAsync(work);

    private static ElevenLabsSpeechClient Create(
        HttpMessageHandler handler,
        ManualClock clock,
        int maxRetries,
        Action<VoiceOptions>? configure = null)
    {
        var options = new VoiceOptions { ApiKey = "FAKE-KEY-RETRY", DefaultVoiceId = "voice-1", MaxRetries = maxRetries };

        configure?.Invoke(options);

        return new ElevenLabsSpeechClient(options, new HttpClient(handler), timeProvider: clock);
    }

    private static SpeechRequest Speech(string text, bool timestamps = false)
        => new() { Text = text, IncludeTimestamps = timestamps };

    /// <summary>A handler that answers the n-th request with the n-th answer, and every later one with the last.</summary>
    private static StubHttpMessageHandler Answers(params Func<HttpRequestMessage, HttpResponseMessage>[] answers)
    {
        var index = -1;

        return new StubHttpMessageHandler(request => answers[Math.Min(Interlocked.Increment(ref index), answers.Length - 1)](request));
    }

    private static HttpResponseMessage Success() => StubHttpMessageHandler.Binary(Mp3Bytes);

    /// <summary>A handler that holds every request until its token is cancelled, and counts the requests it was given.</summary>
    private sealed class BlockedHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _entered;
        private int _observedCancellation;

        public int Entered => Volatile.Read(ref _entered);

        public bool ObservedCancellation => Volatile.Read(ref _observedCancellation) == 1;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _entered);

            try
            {
                await _never.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Exchange(ref _observedCancellation, 1);

                throw;
            }

            throw new InvalidOperationException("The request was never meant to be answered.");
        }
    }

    /// <summary>A body whose connection drops before the first byte, as the provider's does when it cuts a generation off.</summary>
    private sealed class DroppedContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new IOException("The connection was reset.");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;

            return false;
        }
    }

    private static HttpResponseMessage Failure(HttpStatusCode status, string? retryAfter = null, string? characterCost = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent("""{"detail":{"status":"busy","message":"try later"}}"""),
        };

        if (retryAfter is not null)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        }

        if (characterCost is not null)
        {
            response.Headers.TryAddWithoutValidation("character-cost", characterCost);
        }

        return response;
    }
}
