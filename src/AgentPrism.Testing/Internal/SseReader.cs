using System.Runtime.CompilerServices;
using System.Text;

namespace AgentPrism.Testing.Internal;

/// <summary>Bir Server-Sent Events akisini cerceve cerceve okur.</summary>
internal static class SseReader
{
    /// <summary>Akistaki cerceveleri sirayla dondurur.</summary>
    /// <param name="stream">Yanit govdesi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Cozumlenmis cerceveler.</returns>
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

/// <summary>Tek bir SSE cercevesi.</summary>
/// <param name="Event"><c>event</c> alani.</param>
/// <param name="Data"><c>data</c> alani.</param>
internal sealed record SseFrame(string? Event, string Data);
