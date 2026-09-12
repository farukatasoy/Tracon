using System.Runtime.CompilerServices;
using System.Text;

namespace Tracon.Testing.Internal;

/// <summary>Reads a Server-Sent Events stream frame by frame.</summary>
internal static class SseReader
{
    /// <summary>Returns the frames in the stream in order.</summary>
    /// <param name="stream">Response body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parsed frames.</returns>
    public static async IAsyncEnumerable<SseFrame> ReadAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? name = null;
        var data = new StringBuilder();

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                if (name is not null || data.Length > 0)
                {
                    yield return new SseFrame(name, data.ToString());
                }

                name = null;
                data.Clear();
                continue;
            }

            if (line[0] == ':')
            {
                continue;
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                name = line[7..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(line[6..]);
            }
        }
    }
}

/// <summary>A single SSE frame.</summary>
/// <param name="Event">The <c>event</c> field.</param>
/// <param name="Data">The <c>data</c> field.</param>
internal sealed record SseFrame(string? Event, string Data);
