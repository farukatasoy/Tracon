using System.Net;
using System.Text;
using AgentPrism.Client.Generated;

namespace AgentPrism.Client.UnitTests;

/// <summary>
/// The framing rules and content-type guard behind the generated
/// <c>&lt;operation&gt;StreamAsync</c> methods (Phase 159).
/// </summary>
/// <remarks>
/// These live in the hand-written half of the partial class
/// (<c>AgentPrismApiClient.Sse.cs</c>) precisely so they can be tested here:
/// chunk boundaries fall wherever the network puts them, and a decoder that
/// assumes whole frames per read works right up until a slow connection proves
/// it wrong. A real-server test cannot produce adversarial chunking, so it
/// cannot stand in for these — <c>GeneratedClientSseTests</c> proves the
/// generated methods work end to end, this proves the reader is correct.
/// </remarks>
public sealed class AgentPrismApiClientSseTests
{
    [Fact]
    public async Task Each_frame_is_yielded_as_its_own_raw_element()
    {
        var frames = await ReadAllAsync(
            "event: run.started\ndata: {\"a\":1}\n\nevent: run.completed\ndata: {\"b\":2}\n\n");

        frames.ShouldBe([
            "event: run.started\ndata: {\"a\":1}",
            "event: run.completed\ndata: {\"b\":2}",
        ]);
    }

    [Fact]
    public async Task A_frame_split_across_reads_is_reassembled()
    {
        // The chunk boundary lands inside a field name, inside a value, and
        // between the two newlines that terminate the frame.
        var frames = await ReadAllAsync(new ChunkedStream([
            "event: run.st",
            "arted\ndata: {\"a\":",
            "1}\n",
            "\nevent: done\ndata: x\n\n",
        ]));

        frames.ShouldBe(["event: run.started\ndata: {\"a\":1}", "event: done\ndata: x"]);
    }

    [Fact]
    public async Task A_CRLF_split_across_reads_is_one_line_break_not_two()
    {
        var frames = await ReadAllAsync(new ChunkedStream(["event: done\r", "\ndata: x\r\n\r\n"]));

        frames.ShouldBe(["event: done\ndata: x"]);
    }

    [Fact]
    public async Task A_comment_only_block_is_skipped_as_a_keep_alive()
    {
        // AgentPrism's SseWriter sends ": keep-alive\n\n" while a run is still
        // in progress. Surfacing those would make every caller filter them.
        var frames = await ReadAllAsync(": waiting\n\n: waiting\n\nevent: done\ndata: x\n\n");

        frames.ShouldBe(["event: done\ndata: x"]);
    }

    [Fact]
    public async Task A_comment_inside_a_real_frame_stays_in_the_raw_text()
    {
        var frames = await ReadAllAsync(": note\nevent: done\ndata: x\n\n");

        frames.ShouldBe([": note\nevent: done\ndata: x"]);
    }

    [Fact]
    public async Task A_final_frame_with_no_terminating_blank_line_is_still_yielded()
    {
        // A server that closes the connection right after the last frame is
        // within its rights; dropping that frame would lose the run's result.
        var frames = await ReadAllAsync("event: done\ndata: x");

        frames.ShouldBe(["event: done\ndata: x"]);
    }

    [Fact]
    public async Task An_empty_body_yields_no_frames()
    {
        var frames = await ReadAllAsync(string.Empty);

        frames.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancelling_mid_stream_stops_the_enumeration()
    {
        using var cts = new CancellationTokenSource();
        using var response = EventStreamResponse(new BlockingStream("event: first\ndata: x\n\n"));

        var received = new List<string>();

        var act = async () =>
        {
            await foreach (var frame in AgentPrismApiClient.ReadServerSentEventFramesAsync(response, cts.Token))
            {
                received.Add(frame);

                // The stream never ends on its own; without an honored token
                // the next read would hang forever rather than fail.
                await cts.CancelAsync();
            }
        };

        await act.ShouldThrowAsync<OperationCanceledException>();
        received.ShouldBe(["event: first\ndata: x"]);
    }

    [Fact]
    public async Task A_streaming_read_of_a_JSON_answer_is_refused_by_name()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"id\":\"resp_1\"}", Encoding.UTF8, "application/json"),
        };

        var exception = await Should.ThrowAsync<AgentPrismApiException>(
            async () => await AgentPrismApiClient.EnsureContentTypeAsync(
                response, expectServerSentEvents: true, 200, NoHeaders, "Send \"stream\": true.", default));

        // Without this the SSE reader would find no frames in a JSON document
        // and the call would succeed with an empty stream — the silent failure
        // this phase exists to close.
        exception.Message.ShouldContain("application/json");
        exception.Message.ShouldContain("text/event-stream");
        exception.Message.ShouldContain("Send \"stream\": true.");
        exception.StatusCode.ShouldBe(200);
        exception.Response.ShouldBe("{\"id\":\"resp_1\"}");
    }

    [Fact]
    public async Task A_JSON_read_of_a_streaming_answer_is_refused_by_name()
    {
        using var response = EventStreamResponse(new BlockingStream("event: first\ndata: x\n\n"));

        var exception = await Should.ThrowAsync<AgentPrismApiException>(
            async () => await AgentPrismApiClient.EnsureContentTypeAsync(
                response, expectServerSentEvents: false, 200, NoHeaders, "Send \"stream\": false.", default));

        exception.Message.ShouldContain("text/event-stream");
        exception.Message.ShouldContain("Send \"stream\": false.");

        // 🚨 The body is deliberately NOT attached here: reading an event
        // stream to "show what came back" would block until the run finished,
        // turning a diagnostic into a hang. BlockingStream proves it is never
        // read — this test would never return otherwise.
        exception.Response.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task A_matching_content_type_passes_in_both_directions()
    {
        using var stream = EventStreamResponse(new MemoryStream("event: done\ndata: x\n\n"u8.ToArray()));
        using var json = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        await Should.NotThrowAsync(
            async () => await AgentPrismApiClient.EnsureContentTypeAsync(
                stream, expectServerSentEvents: true, 200, NoHeaders, "hint", default));

        await Should.NotThrowAsync(
            async () => await AgentPrismApiClient.EnsureContentTypeAsync(
                json, expectServerSentEvents: false, 200, NoHeaders, "hint", default));
    }

    [Fact]
    public async Task A_connection_that_drops_mid_stream_surfaces_the_error_rather_than_ending_quietly()
    {
        // The subsystem-failure path: the server (or a proxy) breaks the
        // connection after some frames. Ending the enumeration quietly would
        // read as "the run finished" to every caller, which is the worst
        // possible reading of a truncated stream.
        using var response = EventStreamResponse(
            new FailingStream("event: first\ndata: x\n\n"));

        var received = new List<string>();

        var act = async () =>
        {
            await foreach (var frame in AgentPrismApiClient.ReadServerSentEventFramesAsync(response, default))
            {
                received.Add(frame);
            }
        };

        await act.ShouldThrowAsync<IOException>();
        received.ShouldBe(["event: first\ndata: x"]);
    }

    [Fact]
    public void A_charset_parameter_does_not_hide_the_media_type()
    {
        // The server writes "text/event-stream" bare, but a proxy may append
        // "; charset=utf-8". Comparing the whole header value would then send
        // every streaming call down the mismatch path.
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("event: done\ndata: x\n\n", Encoding.UTF8, "text/event-stream"),
        };

        response.Content.Headers.ContentType?.CharSet.ShouldNotBeNull();
        AgentPrismApiClient.IsServerSentEventStream(response).ShouldBeTrue();
    }

    private static readonly IReadOnlyDictionary<string, IEnumerable<string>> NoHeaders =
        new Dictionary<string, IEnumerable<string>>(StringComparer.Ordinal);

    private static async Task<List<string>> ReadAllAsync(string body)
        => await ReadAllAsync(new MemoryStream(Encoding.UTF8.GetBytes(body)));

    private static async Task<List<string>> ReadAllAsync(Stream body)
    {
        using var response = EventStreamResponse(body);

        var frames = new List<string>();

        await foreach (var frame in AgentPrismApiClient.ReadServerSentEventFramesAsync(response, default))
        {
            frames.Add(frame);
        }

        return frames;
    }

    private static HttpResponseMessage EventStreamResponse(Stream body)
    {
        var content = new StreamContent(body);

        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>Hands out one prepared chunk per read, so the caller decides where boundaries fall.</summary>
    private sealed class ChunkedStream(IReadOnlyList<string> chunks) : Stream
    {
        private int _next;

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            if (_next >= chunks.Count)
            {
                return 0;
            }

            var bytes = Encoding.UTF8.GetBytes(chunks[_next++]);

            bytes.CopyTo(buffer.Span);

            return bytes.Length;
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Yields a prefix once, then fails — a connection dropped mid-run.</summary>
    private sealed class FailingStream(string prefix) : Stream
    {
        private bool _sent;

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_sent)
            {
                throw new IOException("The response ended prematurely.");
            }

            _sent = true;

            var bytes = Encoding.UTF8.GetBytes(prefix);

            bytes.CopyTo(buffer.Span);

            return ValueTask.FromResult(bytes.Length);
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Yields a prefix once, then never completes — a run still in progress.</summary>
    private sealed class BlockingStream(string prefix) : Stream
    {
        private bool _sent;

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_sent)
            {
                _sent = true;

                var bytes = Encoding.UTF8.GetBytes(prefix);

                bytes.CopyTo(buffer.Span);

                return bytes.Length;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);

            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
