using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Tracon;

/// <summary>
/// Answers <c>413</c> to a JSON request body longer than <see cref="TraconEndpointOptions.MaxJsonBodyBytes"/>, before a
/// reader that holds the whole body in memory can be asked to.
/// </summary>
/// <remarks>
/// <para>
/// Nothing bounded a body: a 32 MB <c>message</c> was read, parsed, recorded and echoed, about 30 times its size in memory
/// at once, and only the server's own limit (30 MB on Kestrel, unwritten anywhere in this product) stood between a caller
/// and that cost. A limit that lives in each endpoint's reader would miss the endpoints whose body the framework binds, so
/// it sits in front of all of them: the declared length is checked first, and the body stream is wrapped so a body that
/// lies about its length, or has none (chunked), stops at the limit too.
/// </para>
/// <para>
/// Only a JSON body under an endpoint of the Tracon group is limited. An upload (multipart) and a definition import have
/// their own limits, and the protocol endpoints (MCP, A2A, AG-UI) are mapped outside the group.
/// </para>
/// </remarks>
internal static class RequestBodyLimitMiddleware
{
    /// <summary>The title of the problem.</summary>
    internal const string ProblemTitle = "Request body too large";

    public static async Task InvokeAsync(HttpContext httpContext, RequestDelegate next)
    {
        if (httpContext.GetEndpoint()?.Metadata.GetMetadata<JsonBodyLimitMetadata>() is not { } limit
            || !httpContext.Request.HasJsonContentType())
        {
            await next(httpContext).ConfigureAwait(false);

            return;
        }

        if (httpContext.Request.ContentLength is { } declared && declared > limit.MaxBytes)
        {
            await TooLarge(httpContext, limit).ExecuteAsync(httpContext).ConfigureAwait(false);

            return;
        }

        httpContext.Request.Body = new LimitedBodyStream(httpContext.Request.Body, limit.MaxBytes);

        try
        {
            await next(httpContext).ConfigureAwait(false);
        }
        catch (Exception exception) when (!httpContext.Response.HasStarted && FindTooLarge(exception) is not null)
        {
            await TooLarge(httpContext, limit).ExecuteAsync(httpContext).ConfigureAwait(false);
        }
    }

    private static RequestBodyTooLargeException? FindTooLarge(Exception exception)
    {
        // The framework wraps what a body read throws (BadHttpRequestException) and a serializer may wrap it again.
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is RequestBodyTooLargeException tooLarge)
            {
                return tooLarge;
            }
        }

        return null;
    }

    private static IResult TooLarge(HttpContext httpContext, JsonBodyLimitMetadata limit)
    {
        var detail = $"The body may be at most {limit.MaxBytes} bytes.";

        // An OpenAI SDK reads the error envelope of its own API, not a problem document.
        return (httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText?.StartsWith(limit.OpenAIPathPrefix, StringComparison.Ordinal) == true
            ? OpenAICompatSupport.Error(StatusCodes.Status413PayloadTooLarge, detail)
            : TypedResults.Problem(title: ProblemTitle, detail: detail, statusCode: StatusCodes.Status413PayloadTooLarge);
    }

    /// <summary>Thrown by the wrapped body stream when a read passes the limit.</summary>
    /// <remarks>
    /// Not an <see cref="IOException"/>: the framework reads one thrown from a body as a client that went away and
    /// aborts the connection, which would answer nothing at all.
    /// </remarks>
    internal sealed class RequestBodyTooLargeException(long maxBytes)
        : Exception($"The request body is longer than {maxBytes} bytes.");

    /// <summary>A read-only view of the request body that refuses to deliver more than the limit.</summary>
    private sealed class LimitedBodyStream(Stream inner, long maxBytes) : Stream
    {
        private long _read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Count(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count)
            => Count(inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer)
            => Count(inner.Read(buffer));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Count(int read)
        {
            _read += read;

            return _read > maxBytes ? throw new RequestBodyTooLargeException(maxBytes) : read;
        }
    }
}
