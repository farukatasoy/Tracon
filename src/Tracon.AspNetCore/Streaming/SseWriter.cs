using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// Writes Server-Sent Events frames onto an <see cref="HttpResponse"/>.
/// </summary>
/// <remarks>
/// <para>
/// Response buffering is disabled and the stream is flushed after every frame;
/// otherwise events would reach the client only once the response closes.
/// </para>
/// <para>
/// The <c>X-Accel-Buffering: no</c> header stops reverse proxies like nginx from
/// buffering the stream.
/// </para>
/// <para>
/// The writer keeps the stream alive on its own: from <see cref="StartAsync"/>
/// until it is disposed, it writes a keep-alive comment whenever nothing has
/// been written for the endpoint's
/// <see cref="TraconEndpointOptions.StreamKeepAliveInterval"/>. A stream whose
/// producer is busy and quiet (a workflow node, a tool call, a slot wait) sent
/// no bytes at all, and every reader with an idle limit took that silence for
/// a dead connection: the console gives up after 30 seconds, a reverse proxy
/// after its read timeout. The reader then closed the connection, and a closed
/// connection cancels the run — a healthy run was stopped by its own
/// silence. Starting the loop here, not in each endpoint, is what
/// keeps the next SSE endpoint from repeating that.
/// </para>
/// <para>
/// The keep-alive frame is an SSE comment: readers that follow the SSE rules,
/// the OpenAI SDKs among them, drop it without surfacing anything.
/// </para>
/// </remarks>
internal sealed class SseWriter : IAsyncDisposable
{
    private const string KeepAliveFrame = ": waiting\n\n";

    private readonly HttpResponse _response;
    private readonly TimeProvider _timeProvider;

    // 🚨 Every write goes through this gate. The keep-alive loop writes from a
    // background task while the endpoint's own loop (and, on the agent stream,
    // a hook called from inside the agent run) writes frames; two unguarded
    // writes interleave their bytes and corrupt both frames.
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _keepAliveStop;
    private readonly Task _keepAlive;
    private long _lastWriteTimestamp;

    private SseWriter(
        HttpResponse response,
        TimeProvider timeProvider,
        TimeSpan keepAliveInterval,
        CancellationToken cancellationToken)
    {
        _response = response;
        _timeProvider = timeProvider;
        _lastWriteTimestamp = timeProvider.GetTimestamp();
        _keepAliveStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _keepAlive = KeepAliveLoopAsync(keepAliveInterval, _keepAliveStop.Token);
    }

    /// <summary>
    /// Sets up the response headers and returns a writer ready for use. The
    /// writer starts keeping the stream alive at once.
    /// </summary>
    /// <param name="response">The response to write to.</param>
    /// <param name="cancellationToken">
    /// The request's cancellation token. The keep-alive loop stops when it is cancelled.
    /// </param>
    /// <returns>
    /// The writer, ready for use. Dispose it before the response ends:
    /// disposing stops the keep-alive loop and waits for a write in flight, so
    /// no byte lands after the endpoint has returned.
    /// </returns>
    public static async Task<SseWriter> StartAsync(HttpResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache,no-store";
        response.Headers.Pragma = "no-cache";
        response.Headers.ContentEncoding = "identity";

        // Disables reverse proxy buffering (nginx and derivatives).
        response.Headers["X-Accel-Buffering"] = "no";

        response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

        var timeProvider = response.HttpContext.RequestServices?.GetService<TimeProvider>() ?? TimeProvider.System;

        // MapTracon puts the interval on its route group; an endpoint mapped
        // outside it still gets the documented default rather than no keep-alive.
        var keepAliveInterval = response.HttpContext.GetEndpoint()?.Metadata
            .GetMetadata<SseKeepAliveMetadata>()?.Interval ?? TraconEndpointOptions.DefaultStreamKeepAliveInterval;

        return new SseWriter(response, timeProvider, keepAliveInterval, cancellationToken);
    }

    /// <summary>Stops the keep-alive loop and waits for a keep-alive write in flight.</summary>
    /// <returns>The completion task.</returns>
    public async ValueTask DisposeAsync()
    {
        await _keepAliveStop.CancelAsync().ConfigureAwait(false);

        try
        {
            await _keepAlive.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // No filter: the loop is already stopped, and its failure is a
            // keep-alive that could not be written to a response the endpoint is
            // done with (a server can report a completed writer as
            // InvalidOperationException). Rethrowing here, from the endpoint's
            // `await using`, would replace the endpoint's own outcome.
        }

        _keepAliveStop.Dispose();
        _writeGate.Dispose();
    }

    /// <summary>Writes an event frame and flushes the stream.</summary>
    /// <param name="id">
    /// The event id. The client sends this back in the <c>Last-Event-ID</c> header
    /// when the connection drops. If left empty, the <c>id</c> field is not written.
    /// </param>
    /// <param name="eventName">The event name. If left empty, the <c>event</c> field is not written.</param>
    /// <param name="data">The data body. Line breaks are split per the SSE rule.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    public async Task WriteEventAsync(
        long? id,
        string? eventName,
        string data,
        CancellationToken cancellationToken)
    {
        var frame = new StringBuilder();

        if (id is { } sequence)
        {
            frame.Append("id: ").Append(sequence.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        if (!string.IsNullOrEmpty(eventName))
        {
            frame.Append("event: ").Append(eventName).Append('\n');
        }

        // In SSE, every data line carries its own "data: " prefix. Writing a
        // multi-line payload as a single line causes broken parsing on the client.
        foreach (var line in data.AsSpan().EnumerateLines())
        {
            frame.Append("data: ").Append(line).Append('\n');
        }

        frame.Append('\n');

        await WriteRawAsync(frame.ToString(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes an already-formatted frame as-is.</summary>
    /// <param name="frame">
    /// The complete SSE frame. The Microsoft Agent Framework's
    /// <c>OpenAIResponses.WriteResponseStreamAsync</c> output is already in this
    /// format; reframing it would cause corruption.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    public async Task WriteRawAsync(string frame, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _response.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await _response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

            Volatile.Write(ref _lastWriteTimestamp, _timeProvider.GetTimestamp());
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// Writes a comment line. The client ignores it; it keeps the connection and
    /// intermediate proxies alive.
    /// </summary>
    /// <param name="text">The comment text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    public Task WriteKeepAliveAsync(string text, CancellationToken cancellationToken)
        => WriteRawAsync($": {text}\n\n", cancellationToken);

    /// <summary>
    /// Sleeps until the stream has been silent for <paramref name="interval"/>,
    /// then writes a keep-alive comment.
    /// </summary>
    /// <remarks>
    /// The wait is measured from the LAST write, not from the last tick: a
    /// fixed-period timer that skips a tick after a recent frame lets almost
    /// two intervals pass in silence, which is past the console's limit when
    /// the interval is half of it.
    /// </remarks>
    private async Task KeepAliveLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var silence = _timeProvider.GetElapsedTime(Volatile.Read(ref _lastWriteTimestamp));

                if (silence < interval)
                {
                    await Task.Delay(interval - silence, _timeProvider, cancellationToken).ConfigureAwait(false);

                    continue;
                }

                await WriteRawAsync(KeepAliveFrame, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Stopped, or the connection is gone. A keep-alive that cannot be
            // written has nobody to keep alive; the endpoint's own next write
            // meets the same closed connection and handles it there.
        }
    }

    /// <summary>
    /// Reads the <c>Last-Event-ID</c> header and returns the next sequence number.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <returns>
    /// The sequence number to resume reading from. If the header is absent or
    /// cannot be parsed, reading starts from the beginning (<c>0</c>).
    /// </returns>
    /// <remarks>
    /// The header is the id of the <em>last</em> event the client received;
    /// reading must resume from the next event, otherwise the last event is sent
    /// again.
    /// </remarks>
    public static long ReadResumeSequence(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var raw = request.Headers["Last-Event-ID"].ToString();

        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lastId) && lastId >= 0
            ? lastId + 1
            : 0;
    }
}
