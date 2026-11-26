using System.Net;
using Shouldly;
using Tracon.Voice.UnitTests.Infrastructure;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// A speech provider's failure is the type a chat failure is: the status, whether sending the call again can change the answer,
/// and the one machine-readable code the provider's answer carried. Nothing else of the provider's body.
/// </summary>
/// <remarks>
/// A caller that retries (a job queue, a pipeline step) asks one question of a failed call: is it worth sending again. Before,
/// a speech failure answered it only through its message, and the provider's own code, which tells a spent quota from a rate
/// limit, was read and thrown away. The provider's body also echoes the request and can quote a key, so the cases read what
/// is NOT carried as closely as what is.
/// </remarks>
public sealed class ElevenLabsProviderFailureTests
{
    private const string Canary = "CANARY-SPOKEN-TEXT-9921";

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.UnprocessableEntity, false)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    public async Task A_failed_answer_carries_the_status_and_whether_a_retry_can_change_it(HttpStatusCode status, bool transient)
    {
        var failure = await SynthesizeFailureAsync(status, """{"detail":{"status":"some_code","message":"x"}}""");

        failure.Provider.ShouldBe("elevenlabs");
        failure.StatusCode.ShouldBe((int)status);
        failure.IsTransient.ShouldBe(transient);
    }

    [Fact]
    public async Task The_code_of_the_provider_is_carried_and_its_sentence_is_not()
    {
        var failure = await SynthesizeFailureAsync(
            HttpStatusCode.TooManyRequests,
            $$$"""{"detail":{"status":"quota_exceeded","message":"You have {{{Canary}}} credits left"}}""");

        failure.ProviderErrorCode.ShouldBe("quota_exceeded");
        failure.Message.ShouldStartWith("Speech could not be generated: HTTP 429");
        failure.Message.ShouldNotContain(Canary, Case.Sensitive);
        failure.ToString().ShouldNotContain(Canary, Case.Sensitive);
        failure.InnerException!.Message.ShouldNotContain(Canary, Case.Sensitive);
    }

    [Theory]
    [InlineData("""{"detail":{"code":"voice_not_found"}}""", "voice_not_found")]
    [InlineData("""{"detail":{"type":"invalid_request"}}""", "invalid_request")]
    [InlineData("""{"detail":{"status":"first","code":"second"}}""", "first")]
    public async Task The_code_is_read_from_status_then_code_then_type(string body, string expected)
    {
        var failure = await SynthesizeFailureAsync(HttpStatusCode.BadRequest, body);

        failure.ProviderErrorCode.ShouldBe(expected);
    }

    [Theory]
    [InlineData("""{"detail":"a plain sentence"}""")]
    [InlineData("""{"detail":[{"loc":["body"],"msg":"field required"}]}""")]
    [InlineData("""{"detail":{"status":"not a code at all"}}""")]
    [InlineData("""{"detail":{"status":"a/b"}}""")]
    [InlineData("""<html>502 Bad Gateway</html>""")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("\"just a string\"")]
    [InlineData("123")]
    [InlineData("null")]
    public async Task A_body_that_holds_no_code_leaves_it_null_and_still_fails_with_the_status(string body)
    {
        var failure = await SynthesizeFailureAsync(HttpStatusCode.BadRequest, body);

        failure.ProviderErrorCode.ShouldBeNull();
        failure.StatusCode.ShouldBe(400);
    }

    [Fact]
    public async Task A_body_past_sixteen_kilobytes_is_not_read_for_a_code()
    {
        var body = $$$"""{"detail":{"status":"voice_not_found","padding":"{{{new string('x', 17 * 1024)}}}"}}""";

        var failure = await SynthesizeFailureAsync(HttpStatusCode.BadRequest, body);

        failure.ProviderErrorCode.ShouldBeNull("an error answer is small, and a body this size is not one");
        failure.StatusCode.ShouldBe(400);
    }

    [Fact]
    public async Task An_error_body_that_cannot_be_read_is_the_same_failure_with_no_code()
    {
        using var client = DialogueTestData.CreateClient(new StubHttpMessageHandler(
            static _ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StreamContent(new BreakingStream(readableBytes: 0)) }));

        // The streamed call reads the headers first and the error body after, so it is the one that meets a body that breaks.
        var failure = await Should.ThrowAsync<ProviderInvocationException>(
            async () =>
            {
                await foreach (var _ in client.SynthesizeStreamingAsync(new SpeechRequest { Text = "hello" }, TestContext.Current.CancellationToken))
                {
                    // Nothing arrives: the answer is an error.
                }
            });

        failure.StatusCode.ShouldBe(500, "the connection dropping while the error arrived does not turn it into another failure");
        failure.IsTransient.ShouldBeTrue();
        failure.ProviderErrorCode.ShouldBeNull();
    }

    [Fact]
    public async Task A_stream_that_breaks_midway_is_a_transient_failure_with_no_status()
    {
        using var client = DialogueTestData.CreateClient(new StubHttpMessageHandler(
            static _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BreakingStream(readableBytes: 3)) }));

        var failure = await Should.ThrowAsync<ProviderInvocationException>(
            async () =>
            {
                await foreach (var _ in client.SynthesizeStreamingAsync(new SpeechRequest { Text = "hello" }, TestContext.Current.CancellationToken))
                {
                    // Reading to the end is what meets the break.
                }
            });

        failure.StatusCode.ShouldBeNull();
        failure.IsTransient.ShouldBeTrue();
        failure.Message.ShouldContain("interrupted", Case.Sensitive);
    }

    [Fact]
    public async Task A_stream_that_cannot_be_started_is_a_transient_failure_with_no_status_and_no_address()
    {
        using var client = DialogueTestData.CreateClient(new StubHttpMessageHandler(
            static _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (secret-host.internal:443)")));

        var failure = await Should.ThrowAsync<ProviderInvocationException>(
            async () =>
            {
                await foreach (var _ in client.SynthesizeStreamingAsync(new SpeechRequest { Text = "hello" }, TestContext.Current.CancellationToken))
                {
                    // Nothing arrives.
                }
            });

        failure.StatusCode.ShouldBeNull();
        failure.IsTransient.ShouldBeTrue();
        failure.Message.ShouldNotContain("secret-host", Case.Sensitive);
    }

    [Fact]
    public async Task A_sentence_put_where_a_code_belongs_is_dropped_whatever_its_length()
    {
        var longCode = new string('a', 65);

        var failure = await SynthesizeFailureAsync(HttpStatusCode.BadRequest, $$$"""{"detail":{"status":"{{{longCode}}}"}}""");

        failure.ProviderErrorCode.ShouldBeNull("a token past 64 characters is not a code");
    }

    [Fact]
    public async Task A_failed_dialogue_and_a_failed_transcription_are_the_same_type()
    {
        using var dialogue = DialogueTestData.CreateClient(new StubHttpMessageHandler(
            _ => DialogueTestData.Failure(HttpStatusCode.TooManyRequests, """{"detail":{"status":"rate_limited"}}""")));

        var dialogueFailure = await Should.ThrowAsync<ProviderInvocationException>(
            async () => await dialogue.SynthesizeDialogueAsync(DialogueTestData.TwoTurns(), TestContext.Current.CancellationToken));

        dialogueFailure.ProviderErrorCode.ShouldBe("rate_limited");
        dialogueFailure.IsTransient.ShouldBeTrue();

        using var transcription = DialogueTestData.CreateClient(new StubHttpMessageHandler(
            _ => DialogueTestData.Failure(HttpStatusCode.InternalServerError, """{"detail":{"status":"model_down"}}""")));

        using var audio = new MemoryStream(DialogueTestData.Mp3Bytes);

        var transcriptionFailure = await Should.ThrowAsync<ProviderInvocationException>(
            async () => await transcription.TranscribeAsync(audio, "audio/mpeg", options: null, TestContext.Current.CancellationToken));

        transcriptionFailure.ProviderErrorCode.ShouldBe("model_down");
        transcriptionFailure.StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task A_refused_connection_is_a_transient_failure_with_no_status_and_no_address()
    {
        using var client = DialogueTestData.CreateClient(
            new StubHttpMessageHandler(static _ => throw new HttpRequestException(
                HttpRequestError.ConnectionError,
                "Connection refused (secret-host.internal:443)")),
            static options => options.Endpoint = new Uri("https://secret-host.internal/"));

        var failure = await Should.ThrowAsync<ProviderInvocationException>(
            async () => await client.SynthesizeAsync(new SpeechRequest { Text = "hello" }, TestContext.Current.CancellationToken));

        failure.StatusCode.ShouldBeNull();
        failure.IsTransient.ShouldBeTrue();
        failure.Message.ShouldContain("Connection error (ConnectionError)", Case.Sensitive);
        failure.Message.ShouldNotContain("secret-host", Case.Sensitive);
    }

    [Fact]
    public async Task A_call_that_runs_out_of_time_is_a_transient_failure()
    {
        // HttpClient reports its own deadline as a cancellation the caller did not ask for.
        using var client = DialogueTestData.CreateClient(new StubHttpMessageHandler(static _ => throw new TaskCanceledException()));

        var failure = await Should.ThrowAsync<ProviderInvocationException>(
            async () => await client.SynthesizeAsync(new SpeechRequest { Text = "hello" }, TestContext.Current.CancellationToken));

        failure.IsTransient.ShouldBeTrue();
        failure.StatusCode.ShouldBeNull();
        failure.Message.ShouldContain("timed out", Case.Sensitive);
    }

    [Fact]
    public async Task The_health_check_still_names_a_refused_connection_and_a_timeout_in_its_own_words()
    {
        using var refused = DialogueTestData.CreateClient(new StubHttpMessageHandler(
            static _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (secret-host:443)")));
        using var slow = DialogueTestData.CreateClient(new StubHttpMessageHandler(static _ => throw new TaskCanceledException()));

        var first = await refused.CheckHealthAsync(TestContext.Current.CancellationToken);
        var second = await slow.CheckHealthAsync(TestContext.Current.CancellationToken);

        first.Detail.ShouldBe("Connection error (ConnectionError).");
        second.Detail.ShouldBe("Timed out.");
    }

    [Fact]
    public async Task A_cancelled_call_stays_a_cancellation_and_is_not_wrapped()
    {
        using var client = DialogueTestData.CreateClient(new StubHttpMessageHandler(
            static _ => throw new TaskCanceledException()));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await client.SynthesizeAsync(new SpeechRequest { Text = "hello" }, cancelled.Token));
    }

    /// <summary>A body that gives a few bytes and then breaks, as a connection does that drops mid-answer.</summary>
    private sealed class BreakingStream(int readableBytes) : Stream
    {
        private int _given;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_given >= readableBytes)
            {
                throw new IOException("The connection was reset.");
            }

            var count = Math.Min(buffer.Length, readableBytes - _given);

            buffer[..count].Fill(1);
            _given += count;

            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Read(buffer.Span));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static async Task<ProviderInvocationException> SynthesizeFailureAsync(HttpStatusCode status, string body)
    {
        using var client = DialogueTestData.CreateClient(new StubHttpMessageHandler(_ => DialogueTestData.Failure(status, body)));

        return await Should.ThrowAsync<ProviderInvocationException>(
            async () => await client.SynthesizeAsync(new SpeechRequest { Text = Canary }, TestContext.Current.CancellationToken));
    }
}
