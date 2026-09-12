using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

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
/// </remarks>
internal sealed class SseWriter
{
    private readonly HttpResponse _response;

    private SseWriter(HttpResponse response) => _response = response;

    /// <summary>Sets up the response headers and returns a writer ready for use.</summary>
    /// <param name="response">The response to write to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The writer, ready for use.</returns>
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

        return new SseWriter(response);
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
        await _response.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await _response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
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
