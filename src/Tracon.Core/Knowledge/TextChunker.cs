namespace Tracon;

/// <summary>
/// Splits text into fixed-length chunks with overlap.
/// </summary>
/// <remarks>
/// It is intentionally simple. Intelligent chunking based on headings or semantics
/// is outside the library boundary.
/// Consumers can send their own chunks directly.
/// </remarks>
public static class TextChunker
{
    /// <summary>Splits text into chunks with overlap.</summary>
    /// <param name="text">The text to split.</param>
    /// <param name="chunkSize">The chunk length in characters. It must be positive.</param>
    /// <param name="chunkOverlap">
    /// The overlap length between consecutive chunks, in characters. It must be less
    /// than <paramref name="chunkSize"/>; otherwise, the method cannot progress.
    /// </param>
    /// <returns>The chunk text in order, or an empty list for empty text.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="chunkSize"/> is zero or negative, or <paramref name="chunkOverlap"/>
    /// is negative or greater than or equal to <paramref name="chunkSize"/>.
    /// </exception>
    public static IReadOnlyList<string> Split(string text, int chunkSize, int chunkOverlap)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(chunkSize, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkOverlap);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkOverlap, chunkSize);

        if (text.Length == 0)
        {
            return [];
        }

        var step = chunkSize - chunkOverlap;
        var chunks = new List<string>();

        for (var start = 0; start < text.Length; start += step)
        {
            var length = Math.Min(chunkSize, text.Length - start);
            chunks.Add(text.Substring(start, length));

            if (start + length >= text.Length)
            {
                break;
            }
        }

        return chunks;
    }
}
