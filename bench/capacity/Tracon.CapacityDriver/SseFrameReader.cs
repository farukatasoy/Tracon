using System.Diagnostics;
using System.Text;

namespace Tracon.CapacityDriver;

/// <summary>One server-sent event as it arrived.</summary>
/// <param name="Id">The <c>id:</c> field, when present.</param>
/// <param name="EventName">The <c>event:</c> field, or <c>message</c> when absent.</param>
/// <param name="Data">The joined <c>data:</c> lines.</param>
/// <param name="ElapsedMilliseconds">Milliseconds from the request's dispatch, on the driver's clock.</param>
public readonly record struct SseFrame(string? Id, string EventName, string Data, double ElapsedMilliseconds);

/// <summary>Reads an SSE body frame by frame, measuring as it goes.</summary>
/// <remarks>
/// <para>
/// 🚨 This consumes the stream for real. Reading the whole body into a string
/// and counting <c>event:</c> lines afterwards would measure serialization,
/// not streaming, and would report the same time to first content as the
/// buffered scenario - which is exactly the false equivalence the phase
/// forbids.
/// </para>
/// <para>
/// A body that ends in the middle of a frame is reported through
/// <see cref="TruncatedFrame"/>. That is a finding, not a tidy end of stream:
/// a lost final frame is one of the failure modes this apparatus exists to
/// catch.
/// </para>
/// </remarks>
public sealed class SseFrameReader
{
    private readonly long _startTimestamp;

    /// <summary>Creates a reader whose clock starts at the request's dispatch.</summary>
    /// <param name="startTimestamp">The <see cref="Stopwatch.GetTimestamp"/> value taken at dispatch.</param>
    public SseFrameReader(long startTimestamp) => _startTimestamp = startTimestamp;

    /// <summary>Whether the body ended mid-frame.</summary>
    public bool TruncatedFrame { get; private set; }

    /// <summary>Reads every frame from a stream.</summary>
    /// <param name="stream">The response body.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The frames, in order.</returns>
    public async IAsyncEnumerable<SseFrame> ReadAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? id = null;
        string? eventName = null;
        var data = new StringBuilder();
        var pending = false;

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                if (pending)
                {
                    yield return new SseFrame(
                        id,
                        eventName ?? "message",
                        data.ToString(),
                        Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds);

                    id = null;
                    eventName = null;
                    data.Clear();
                    pending = false;
                }

                continue;
            }

            if (line.StartsWith(':'))
            {
                // A comment line. The server uses these as keep-alives; they
                // are not frames and must not be counted as content.
                continue;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            var field = separator < 0 ? line : line[..separator];
            var value = separator < 0 ? "" : line[(separator + 1)..].TrimStart();

            switch (field)
            {
                case "id":
                    id = value;
                    pending = true;
                    break;
                case "event":
                    eventName = value;
                    pending = true;
                    break;
                case "data":
                    if (data.Length > 0)
                    {
                        data.Append('\n');
                    }

                    data.Append(value);
                    pending = true;
                    break;
                default:
                    break;
            }
        }

        if (pending)
        {
            // The body ended without the blank line that closes a frame. The
            // partial frame is NOT yielded - it is recorded as truncation.
            TruncatedFrame = true;
        }
    }
}
