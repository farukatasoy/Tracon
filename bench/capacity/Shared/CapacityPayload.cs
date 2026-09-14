using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Tracon.Capacity;

/// <summary>The deterministic synthetic payload both sides of the measurement compute.</summary>
/// <remarks>
/// <para>
/// 🚨 Linked into host, driver and acceptance suite, never copied. The whole
/// correctness half of the run rests on the two sides deriving the SAME
/// expected answer from the same correlation value: if they drifted, the
/// reconciliation would report content mismatches that are really a
/// divergence in this file.
/// </para>
/// <para>
/// Every value here is <strong>experiment input</strong>. None of it is a
/// measured production traffic shape, and no number produced from it describes
/// what a real workload costs.
/// </para>
/// </remarks>
public static class CapacityPayload
{
    /// <summary>The code tool the agent calls once per run.</summary>
    public const string ToolName = "capacity_probe";

    /// <summary>The marker that opens a request message, so the host can find the correlation.</summary>
    public const string CorrelationMarker = "correlation=";

    private const string Filler = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>Builds the request message for one run.</summary>
    /// <param name="correlation">This request's unique correlation value.</param>
    /// <param name="bytes">How many bytes of UTF-8 the message should be.</param>
    /// <returns>The message.</returns>
    public static string RequestMessage(string correlation, int bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlation);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bytes, 0);

        var head = CorrelationMarker + correlation + " ";
        return Pad(head, bytes);
    }

    /// <summary>Reads the correlation value back out of a request message.</summary>
    /// <param name="message">The message the host received.</param>
    /// <returns>The correlation value, or an empty string when the message does not carry one.</returns>
    public static string ExtractCorrelation(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return "";
        }

        var start = message.IndexOf(CorrelationMarker, StringComparison.Ordinal);

        if (start < 0)
        {
            return "";
        }

        start += CorrelationMarker.Length;
        var end = message.IndexOf(' ', start);
        return end < 0 ? message[start..] : message[start..end];
    }

    /// <summary>The argument the model passes to the code tool.</summary>
    /// <param name="correlation">The run's correlation value.</param>
    /// <returns>The argument.</returns>
    public static string ToolArgument(string correlation) => CorrelationMarker + correlation;

    /// <summary>The deterministic result the code tool returns.</summary>
    /// <param name="correlation">The run's correlation value.</param>
    /// <param name="bytes">How many bytes the result should be.</param>
    /// <returns>The result.</returns>
    public static string ToolResult(string correlation, int bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlation);

        if (bytes <= 0)
        {
            return "";
        }

        return Pad("probe:" + correlation + " ", bytes);
    }

    /// <summary>The chunks the final answer is streamed as.</summary>
    /// <param name="correlation">The run's correlation value.</param>
    /// <param name="chunks">How many chunks.</param>
    /// <param name="chunkCharacters">How many ASCII characters per chunk.</param>
    /// <returns>The chunks, in order.</returns>
    public static IReadOnlyList<string> AnswerChunks(string correlation, int chunks, int chunkCharacters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlation);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(chunks, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(chunkCharacters, 0);

        var result = new List<string>(chunks);

        for (var index = 0; index < chunks; index++)
        {
            var head = string.Create(
                CultureInfo.InvariantCulture,
                $"{correlation}#{index}:");

            result.Add(Pad(head, chunkCharacters));
        }

        return result;
    }

    /// <summary>The whole final answer.</summary>
    /// <param name="correlation">The run's correlation value.</param>
    /// <param name="chunks">How many chunks.</param>
    /// <param name="chunkCharacters">How many ASCII characters per chunk.</param>
    /// <returns>The answer.</returns>
    public static string Answer(string correlation, int chunks, int chunkCharacters)
        => string.Concat(AnswerChunks(correlation, chunks, chunkCharacters));

    /// <summary>The checksum the two sides compare instead of the whole answer.</summary>
    /// <param name="text">The text to hash.</param>
    /// <returns>Lowercase hexadecimal SHA-256.</returns>
    public static string Checksum(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static string Pad(string head, int length)
    {
        if (head.Length >= length)
        {
            return head[..length];
        }

        var builder = new StringBuilder(length);
        builder.Append(head);

        while (builder.Length < length)
        {
            builder.Append(Filler[builder.Length % Filler.Length]);
        }

        return builder.ToString();
    }
}
