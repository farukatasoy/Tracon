using System.Text;

namespace AgentPrism;

/// <summary>
/// Trims text to a UTF-8 byte limit.
/// </summary>
/// <remarks>
/// Pure and stateless. Used both to bound a tool's result
/// (<see cref="TruncatingAIFunction"/>) and an MCP resource's content
/// (<c>AgentPrism.Mcp</c>).
/// </remarks>
public static class TextTrimming
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Trims text to a UTF-8 byte limit. Never cuts in the middle of a
    /// multi-byte character; in that case, the whole character is dropped.
    /// </summary>
    /// <param name="text">The text to trim.</param>
    /// <param name="maxBytes">The maximum byte limit.</param>
    /// <returns>The trimmed text and whether trimming occurred.</returns>
    public static (string Text, bool Truncated) Trim(string text, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (maxBytes <= 0)
        {
            return (string.Empty, text.Length > 0);
        }

        var byteCount = Encoding.UTF8.GetByteCount(text);

        if (byteCount <= maxBytes)
        {
            return (text, false);
        }

        var bytes = Encoding.UTF8.GetBytes(text);

        // A character is at most 4 bytes in UTF-8; if the cut point lands in
        // the middle of an invalid sequence, a valid boundary is found by
        // backing off at most 3 bytes.
        for (var length = maxBytes; length > 0 && length > maxBytes - 4; length--)
        {
            try
            {
                return (StrictUtf8.GetString(bytes, 0, length), true);
            }
            catch (DecoderFallbackException)
            {
            }
        }

        return (string.Empty, true);
    }
}
