using System.Text;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>Reads a Server-Sent Events stream frame by frame.</summary>
internal static class SseReader
{
    /// <summary>Returns the frames in the stream in order.</summary>
    /// <param name="stream">The response body.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resolved frames.</returns>
    public static async IAsyncEnumerable<SseFrame> ReadAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? id = null;
        string? name = null;
        var data = new StringBuilder();

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                if (name is not null || data.Length > 0)
                {
                    yield return new SseFrame(id, name, data.ToString());
                }

                id = null;
                name = null;
                data.Clear();
                continue;
            }

            // Comment line (keep-alive). Not a frame.
            if (line[0] == ':')
            {
                continue;
            }

            if (line.StartsWith("id: ", StringComparison.Ordinal))
            {
                id = line[4..];
            }
            else if (line.StartsWith("event: ", StringComparison.Ordinal))
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

    /// <summary>Reads the stream to completion and lists the frames.</summary>
    /// <param name="stream">The response body.</param>
    /// <returns>All frames.</returns>
    public static async Task<List<SseFrame>> ReadAllAsync(Stream stream)
    {
        var frames = new List<SseFrame>();

        await foreach (var frame in ReadAsync(stream))
        {
            frames.Add(frame);
        }

        return frames;
    }
}

/// <summary>A single SSE frame.</summary>
/// <param name="Id">The <c>id</c> field.</param>
/// <param name="Event">The <c>event</c> field.</param>
/// <param name="Data">The <c>data</c> field.</param>
internal sealed record SseFrame(string? Id, string? Event, string Data);
