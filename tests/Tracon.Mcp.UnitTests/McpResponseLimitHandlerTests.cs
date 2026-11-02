using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// A response larger than <see cref="TraconMcpOptions.MaxResponseBytes"/> fails before the SDK holds it, and a stream of events is
/// counted per event.
/// </summary>
/// <remarks>
/// The SDK reads a response to its end and builds the whole message from it: a 20 MB tool result cost about 500 MB. The handler sits
/// in the <see cref="HttpClient"/> pipeline, so these cases drive it directly with a stub below it; the transport that carries it
/// is checked in <c>McpResponseLimitTransportTests</c>.
/// </remarks>
public sealed class McpResponseLimitHandlerTests
{
    private const int Limit = 1000;

    [Fact]
    public async Task A_body_within_the_limit_is_passed_on_unchanged_with_its_headers()
    {
        var body = new string('a', Limit);

        using var response = await SendAsync(JsonOf(body, declareLength: false), HttpCompletionOption.ResponseHeadersRead);

        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(body);
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/json");
    }

    [Fact]
    public async Task A_body_that_declares_a_length_over_the_limit_is_refused_before_it_is_read()
    {
        var content = new StubContent(new MemoryStream(new byte[Limit + 1]), "application/json", declareLength: true);

        var exception = await Should.ThrowAsync<HttpRequestException>(
            () => SendAsync(content, HttpCompletionOption.ResponseHeadersRead));

        exception.Message.ShouldContain("Tracon:Mcp:MaxResponseBytes");
        content.WasRead.ShouldBeFalse("the length was enough to refuse it");
    }

    [Fact]
    public async Task A_body_with_no_declared_length_is_refused_when_it_passes_the_limit()
    {
        using var response = await SendAsync(JsonOf(new string('a', Limit + 1), declareLength: false), HttpCompletionOption.ResponseHeadersRead);

        await ShouldBeRefusedAsync(() => response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_stream_the_SDK_reads_throws_an_IOException_that_names_the_limit()
    {
        using var response = await SendAsync(JsonOf(new string('a', Limit + 1), declareLength: false), HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);

        var exception = await Should.ThrowAsync<IOException>(async () =>
        {
            var buffer = new byte[4096];

            while (await stream.ReadAsync(buffer, TestContext.Current.CancellationToken) > 0)
            {
            }
        });

        exception.Message.ShouldContain("Tracon:Mcp:MaxResponseBytes");
    }

    [Fact]
    public async Task A_buffered_read_of_a_body_over_the_limit_fails_the_same_way()
    {
        var content = JsonOf(new string('a', Limit + 1), declareLength: false);

        await Should.ThrowAsync<Exception>(() => SendAsync(content, HttpCompletionOption.ResponseContentRead));
    }

    [Fact]
    public async Task A_stream_of_events_is_counted_per_event_so_it_can_run_for_ever()
    {
        // 200 events of 100 bytes each: twenty times the limit in all, none of them over it.
        var stream = string.Concat(Enumerable.Repeat("data: " + new string('x', 90) + "\n\n", 200));

        using var response = await SendAsync(EventsOf(stream), HttpCompletionOption.ResponseHeadersRead);

        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(stream);
    }

    [Theory]
    [InlineData("\n\n")]
    [InlineData("\r\n\r\n")]
    [InlineData("\r\r")]
    public async Task An_event_over_the_limit_is_refused_whichever_way_the_blank_line_is_written(string blank)
    {
        var line = "data: " + new string('x', 400) + blank[..(blank.Length / 2)];
        var oneEvent = string.Concat(Enumerable.Repeat(line, 3)) + blank[(blank.Length / 2)..];

        // Three lines of 400 bytes without a blank line between them are one event of more than the limit.
        using var response = await SendAsync(EventsOf(oneEvent), HttpCompletionOption.ResponseHeadersRead);

        await ShouldBeRefusedAsync(() => response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("\n\n")]
    [InlineData("\r\n\r\n")]
    public async Task Events_that_each_fit_are_passed_whichever_way_the_blank_line_is_written(string blank)
    {
        var stream = string.Concat(Enumerable.Repeat("data: " + new string('x', 700) + blank, 5));

        using var response = await SendAsync(EventsOf(stream), HttpCompletionOption.ResponseHeadersRead);

        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(stream);
    }

    [Fact]
    public async Task A_stream_that_declares_a_length_is_not_refused_for_it()
    {
        // Counting per event is the rule for a stream, whatever its length says.
        var stream = string.Concat(Enumerable.Repeat("data: " + new string('x', 90) + "\n\n", 200));
        var content = new StubContent(new MemoryStream(Encoding.UTF8.GetBytes(stream)), "text/event-stream", declareLength: true);

        using var response = await SendAsync(content, HttpCompletionOption.ResponseHeadersRead);

        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(stream);
    }

    [Fact]
    public void The_limit_defaults_to_sixteen_mebibytes_and_a_value_below_one_stops_the_host()
    {
        new TraconMcpOptions().MaxResponseBytes.ShouldBe(16 * 1024 * 1024);

        var result = new TraconMcpOptionsValidator().Validate(null, new TraconMcpOptions { MaxResponseBytes = 0 });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(TraconMcpOptions.MaxResponseBytes));
    }

    /// <summary>A buffered read wraps the IOException of the stream in an HttpRequestException; the limit is named inside it.</summary>
    private static async Task ShouldBeRefusedAsync(Func<Task> read)
    {
        var exception = await Should.ThrowAsync<Exception>(read);

        for (var inner = exception; inner is not null; inner = inner.InnerException)
        {
            if (inner.Message.Contains("Tracon:Mcp:MaxResponseBytes", StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new ShouldAssertException($"No exception in the chain names the limit: {exception}");
    }

    private static StubContent JsonOf(string body, bool declareLength)
        => new(new MemoryStream(Encoding.UTF8.GetBytes(body)), "application/json", declareLength);

    private static StubContent EventsOf(string events)
        => new(new MemoryStream(Encoding.UTF8.GetBytes(events)), "text/event-stream", declareLength: false);

    private static async Task<HttpResponseMessage> SendAsync(StubContent content, HttpCompletionOption completion)
    {
        using var client = new HttpClient(new McpResponseLimitHandler(Limit) { InnerHandler = new StubHandler(content) });

        return await client.GetAsync(new Uri("http://127.0.0.1/mcp"), completion, TestContext.Current.CancellationToken);
    }

    private sealed class StubHandler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }

    /// <summary>A body that can only be read forward, so that it declares a length only when told to.</summary>
    private sealed class StubContent : HttpContent
    {
        private readonly Stream _source;
        private readonly bool _declareLength;

        public StubContent(Stream source, string mediaType, bool declareLength)
        {
            _source = new ForwardOnlyStream(source, this);
            _declareLength = declareLength;
            Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        }

        public bool WasRead { get; set; }

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(_source);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => _source.CopyToAsync(stream);

        protected override bool TryComputeLength(out long length)
        {
            length = _declareLength ? _source.Length : 0;

            return _declareLength;
        }
    }

    private sealed class ForwardOnlyStream(Stream inner, StubContent owner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            owner.WasRead = true;

            // Hand the bytes over a few at a time so that an event is split across reads, as a socket splits it.
            return inner.Read(buffer, offset, Math.Min(count, 37));
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
