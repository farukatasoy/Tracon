using System.Text;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>Bir Server-Sent Events akisini cerceve cerceve okur.</summary>
internal static class SseReader
{
    /// <summary>Akistaki cerceveleri sirayla dondurur.</summary>
    /// <param name="stream">Yanit govdesi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Cozumlenmis cerceveler.</returns>
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

            // Yorum satiri (keep-alive). Cerceve degil.
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

    /// <summary>Akisi tamamen okur ve cerceveleri listeler.</summary>
    /// <param name="stream">Yanit govdesi.</param>
    /// <returns>Tum cerceveler.</returns>
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

/// <summary>Tek bir SSE cercevesi.</summary>
/// <param name="Id"><c>id</c> alani.</param>
/// <param name="Event"><c>event</c> alani.</param>
/// <param name="Data"><c>data</c> alani.</param>
internal sealed record SseFrame(string? Id, string? Event, string Data);
