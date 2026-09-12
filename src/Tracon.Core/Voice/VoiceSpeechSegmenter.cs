using System.Buffers;
using System.Text;

namespace Tracon;

/// <summary>
/// Splits streaming text into speakable segments.
/// </summary>
/// <remarks>
/// <para>
/// This is the source of latency. If the whole response were awaited and
/// spoken all at once, the user would hear a long silence; if every token
/// were spoken separately, the audio would sound choppy and the provider
/// call count would explode. The sentence boundary is the right point between the two.
/// </para>
/// <para>
/// The splitter is <strong>pure</strong>: there is no network, audio, or
/// time. Unit testing is therefore cheap, and latency behavior is verified
/// independently of the rest of the code.
/// </para>
/// </remarks>
internal sealed class VoiceSpeechSegmenter
{
    /// <summary>
    /// The minimum characters required for a segment to be spoken.
    /// </summary>
    /// <remarks>
    /// A single-word segment like "Yes." would sound choppy if spoken on its
    /// own; such a segment is merged with the next one.
    /// </remarks>
    private const int MinSegmentLength = 12;

    /// <summary>
    /// The length at which a split happens even if no sentence boundary arrived.
    /// </summary>
    /// <remarks>
    /// A model that does not use punctuation (or a response producing a
    /// list) would otherwise never split, and would wait for the end of the
    /// entire response before the first audio.
    /// </remarks>
    private const int MaxSegmentLength = 240;

    private static readonly SearchValues<char> Terminators = SearchValues.Create(".!?;:\n…");

    private readonly StringBuilder _pending = new();

    /// <summary>Appends a new text chunk and returns any segments that are ready.</summary>
    /// <param name="delta">A text chunk from the model.</param>
    /// <returns>Segments ready to be spoken; empty if none.</returns>
    public IReadOnlyList<string> Append(string? delta)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return [];
        }

        _pending.Append(delta);

        List<string>? ready = null;

        while (TryCut(out var segment))
        {
            (ready ??= []).Add(segment);
        }

        return (IReadOnlyList<string>?)ready ?? [];
    }

    /// <summary>Returns the remaining text as a segment and clears the buffer.</summary>
    /// <returns>The remaining segment; <see langword="null"/> if none.</returns>
    public string? Flush()
    {
        var text = _pending.ToString().Trim();
        _pending.Clear();

        return text.Length > 0 ? text : null;
    }

    private bool TryCut(out string segment)
    {
        segment = string.Empty;

        var text = _pending.ToString();
        var cut = FindCut(text);

        if (cut <= 0)
        {
            return false;
        }

        segment = text[..cut].Trim();
        _pending.Remove(0, cut);

        if (segment.Length != 0)
        {
            return true;
        }

        // Only whitespace was cut; a cut occurred, but there is nothing to
        // speak. Returning true just to advance the loop would be wrong.
        return TryCut(out segment);
    }

    /// <summary>Finds the cut point.</summary>
    /// <param name="text">The pending text.</param>
    /// <returns>The number of characters to cut; <c>0</c> if no cut should occur.</returns>
    private static int FindCut(string text)
    {
        var span = text.AsSpan();

        for (var i = MinSegmentLength - 1; i < span.Length; i++)
        {
            if (!Terminators.Contains(span[i]))
            {
                continue;
            }

            // Ambiguous-ending punctuation (e.g. "3." or an abbreviation) is
            // NOT COUNTED as a sentence end until the next character arrives;
            // we decide by waiting.
            if (i + 1 >= span.Length)
            {
                break;
            }

            if (char.IsWhiteSpace(span[i + 1]) || span[i] == '\n')
            {
                return i + 1;
            }
        }

        if (span.Length < MaxSegmentLength)
        {
            return 0;
        }

        // No punctuation arrived: cut at the last space, do not split a word in the middle.
        var window = span[..MaxSegmentLength];
        var lastSpace = window.LastIndexOf(' ');

        return lastSpace > MinSegmentLength ? lastSpace + 1 : MaxSegmentLength;
    }
}
