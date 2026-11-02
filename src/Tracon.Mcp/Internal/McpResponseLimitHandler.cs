using System.Net;
using System.Net.Http.Headers;

namespace Tracon;

/// <summary>
/// Refuses an MCP server's response that is larger than <see cref="TraconMcpOptions.MaxResponseBytes"/>, before the SDK
/// has read all of it into memory.
/// </summary>
/// <remarks>
/// <para>
/// The SDK reads a response to the end and builds the whole message from it: a 20 MB <c>tools/call</c> result cost about
/// 500 MB of memory before <c>DefaultMaxOutputBytes</c> could trim it, and a server nobody controls can send more. The
/// check sits in the <see cref="HttpClient"/> pipeline, so it covers every request of the transport and the SDK is not
/// patched.
/// </para>
/// <para>
/// A JSON response is counted whole. A <c>text/event-stream</c> response is counted per event, from one blank line to the
/// next: the stream a client keeps open for notifications has no end, and a total would kill it once enough had passed.
/// A body that declares a length above the limit is refused without reading a byte.
/// </para>
/// </remarks>
internal sealed class McpResponseLimitHandler(long maxBytes) : DelegatingHandler
{
    private const string EventStream = "text/event-stream";

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        try
        {
            var original = response.Content;
            var streaming = string.Equals(original.Headers.ContentType?.MediaType, EventStream, StringComparison.OrdinalIgnoreCase);

            if (!streaming && original.Headers.ContentLength > maxBytes)
            {
                throw new HttpRequestException(TooLarge(original.Headers.ContentLength.Value, whole: true), inner: null, HttpStatusCode.RequestEntityTooLarge);
            }

            response.Content = new LimitedContent(
                original,
                await original.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                maxBytes,
                streaming);

            return response;
        }
        catch
        {
            response.Dispose();

            throw;
        }
    }

    private static string TooLarge(long size, bool whole)
        => $"The MCP server's response is larger than the limit: {(whole ? "the body declares" : "an event passed")} {size} bytes. " +
           "The limit is changed with Tracon:Mcp:MaxResponseBytes.";

    private sealed class LimitedContent : HttpContent
    {
        private readonly HttpContent _original;
        private readonly Stream _source;
        private readonly long _maxBytes;
        private readonly bool _streaming;

        public LimitedContent(HttpContent original, Stream source, long maxBytes, bool streaming)
        {
            _original = original;
            _source = source;
            _maxBytes = maxBytes;
            _streaming = streaming;

            foreach (var header in original.Headers)
            {
                Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromResult<Stream>(new LimitedStream(_source, _maxBytes, _streaming));

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            => CreateContentReadStreamAsync();

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            using var limited = new LimitedStream(_source, _maxBytes, _streaming);
            await limited.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;

            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _original.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>A read-only view of a response stream that throws when the response grows past the limit.</summary>
    private sealed class LimitedStream(Stream inner, long maxBytes, bool perEvent) : Stream
    {
        // Bytes of the response so far, or of the event so far when counting per event.
        private long _counted;

        private long _lineLength;
        private bool _afterCarriageReturn;

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
            var read = inner.Read(buffer);
            Count(buffer[..read]);

            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Count(buffer.Span[..read]);

            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private void Count(ReadOnlySpan<byte> chunk)
        {
            if (!perEvent)
            {
                _counted += chunk.Length;

                if (_counted > maxBytes)
                {
                    throw new IOException(TooLarge(_counted, whole: false));
                }

                return;
            }

            foreach (var value in chunk)
            {
                _counted++;

                if (value == (byte)'\n' && _afterCarriageReturn)
                {
                    // The second half of CRLF: the carriage return already ended the line.
                    _afterCarriageReturn = false;

                    continue;
                }

                _afterCarriageReturn = value == (byte)'\r';

                if (value is (byte)'\n' or (byte)'\r')
                {
                    // A blank line ends the event, and the next one starts counting from nothing.
                    if (_lineLength == 0)
                    {
                        _counted = 0;
                    }

                    _lineLength = 0;
                }
                else
                {
                    _lineLength++;
                }

                if (_counted > maxBytes)
                {
                    throw new IOException(TooLarge(_counted, whole: false));
                }
            }
        }
    }
}
