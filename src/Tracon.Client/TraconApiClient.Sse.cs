using System.Runtime.CompilerServices;
using System.Text;

namespace Tracon.Client.Generated;

// The runtime half of nswag-postprocess-client.py's streaming-sibling pass.
//
// Every operation whose 200 response declares text/event-stream gets a
// generated `<operation>StreamAsync` sibling returning
// IAsyncEnumerable<string>, one raw SSE frame per element. The generated
// method keeps NSwag's own URL building, body serialization, PrepareRequest/
// ProcessResponse hooks and typed error branches; only its 200 branch is
// rewritten to call the two members below. Framing rules and the content-type
// guard live HERE, hand-written, rather than being emitted as a blob by a
// Python regex - they are the part with real behavior to test
// (TraconApiClientSseTests), and a `dotnet nswag run` regeneration must
// not be able to change them.
//
// These members are `internal`, not `protected`: they are an implementation
// detail of the generated code, and src/Directory.Build.props already grants
// Tracon.Client.UnitTests internals access, so they are unit-testable
// without widening the package's surface.
public partial class TraconApiClient
{
    private const string EventStreamMediaType = "text/event-stream";

    /// <summary>The media type an operation answers with when it streams.</summary>
    /// <remarks>
    /// Read from the CONTENT headers, not the response headers: Content-Type
    /// belongs to the entity body and <see cref="HttpResponseMessage.Content"/>
    /// is nullable per its own contract.
    /// </remarks>
    internal static bool IsServerSentEventStream(HttpResponseMessage response)
        => string.Equals(
            response?.Content?.Headers.ContentType?.MediaType,
            EventStreamMediaType,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Names the media type a response actually carried, for a diagnostic message.</summary>
    internal static string DescribeMediaType(HttpResponseMessage response)
    {
        var mediaType = response?.Content?.Headers.ContentType?.MediaType;

        return string.IsNullOrEmpty(mediaType) ? "(none)" : mediaType!;
    }

    /// <summary>
    /// Fails a 200 whose content type does not match the shape the calling
    /// method reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>/v1/responses</c> and <c>/v1/chat/completions</c> answer with JSON or
    /// with SSE depending on the <c>stream</c> flag in the REQUEST BODY, chosen
    /// at run time. No OpenAPI document can describe a conditional response
    /// shape and no generated signature can enforce one, so the two shapes are
    /// two methods and the mismatch is caught here - on what the server
    /// actually answered, never by rewriting the caller's body, which would be
    /// a worse surprise than a clear failure.
    /// </para>
    /// <para>
    /// The body is attached to the exception only when the response is NOT an
    /// event stream. Reading an event stream to "see what came back" would
    /// block until the run finished, turning a diagnostic into a hang.
    /// </para>
    /// </remarks>
    internal static async Task EnsureContentTypeAsync(
        HttpResponseMessage response,
        bool expectServerSentEvents,
        int statusCode,
        IReadOnlyDictionary<string, IEnumerable<string>> headers,
        string hint,
        CancellationToken cancellationToken)
    {
        var isEventStream = IsServerSentEventStream(response);

        if (isEventStream == expectServerSentEvents)
        {
            return;
        }

        var expected = expectServerSentEvents ? EventStreamMediaType : "application/json";

        var body = isEventStream || response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        throw new TraconApiException(
            $"The server answered 200 with content type '{DescribeMediaType(response)}', not '{expected}'. {hint}",
            statusCode,
            body,
            headers,
            null);
    }

    /// <summary>
    /// <see cref="ReadServerSentEventFramesAsync"/>, already configured not to
    /// capture the synchronization context.
    /// </summary>
    /// <remarks>
    /// <c>ConfigureAwait</c> on an <see cref="IAsyncEnumerable{T}"/> is an
    /// EXTENSION method (<c>System.Threading.Tasks.TaskAsyncEnumerableExtensions</c>),
    /// so it only resolves where that namespace is imported - and the generated
    /// client imports no namespace at all, it fully qualifies every type. This
    /// wrapper moves the extension call into a file that DOES import it, so the
    /// generated <c>await foreach</c> stays a plain, resolvable call and library
    /// code still does not capture the context.
    /// </remarks>
    internal static ConfiguredCancelableAsyncEnumerable<string> ReadServerSentEventFramesConfigured(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
        => ReadServerSentEventFramesAsync(response, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Reads a <c>text/event-stream</c> body as raw Server-Sent Events frames,
    /// yielding each one as the server flushes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One element is one complete frame: the frame's own lines joined with
    /// <c>\n</c>, without the blank line that terminates it. The frame is NOT
    /// parsed into id/event/data fields - the payload of <c>/v1/responses</c>
    /// and <c>/v1/chat/completions</c> is OpenAI's schema, not Tracon's, and
    /// a typed wrapper here would go stale the moment OpenAI adds an event.
    /// </para>
    /// <para>
    /// Comment-only blocks are skipped. Tracon sends <c>: keep-alive</c>
    /// comments while a run is still in progress (<c>SseWriter</c>); the SSE
    /// specification says a client ignores them, and surfacing them would make
    /// every caller filter them out.
    /// </para>
    /// <para>
    /// <see cref="StreamReader"/> does the line splitting, so a frame that
    /// arrives split across TCP chunks - or a <c>\r\n</c> whose halves land in
    /// different reads - is reassembled rather than mis-framed. A final frame
    /// with no terminating blank line is still yielded at end of stream.
    /// </para>
    /// </remarks>
    internal static async IAsyncEnumerable<string> ReadServerSentEventFramesAsync(
        HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (response.Content is null)
        {
            yield break;
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        // The reader owns the stream and disposes it; the response itself is
        // disposed by the generated method's own finally block, which runs when
        // the caller disposes the enumerator - including on an early `break`.
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var frame = new StringBuilder();
        var hasField = false;

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                if (hasField)
                {
                    yield return frame.ToString();
                }

                frame.Clear();
                hasField = false;
                continue;
            }

            if (frame.Length > 0)
            {
                frame.Append('\n');
            }

            frame.Append(line);

            // A block of nothing but comment lines is a keep-alive, not an event.
            if (!line.StartsWith(':'))
            {
                hasField = true;
            }
        }

        if (hasField)
        {
            yield return frame.ToString();
        }
    }
}
