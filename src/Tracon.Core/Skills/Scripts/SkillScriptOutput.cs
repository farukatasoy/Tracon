using System.Text;

namespace Tracon;

/// <summary>The one output limit every script result passes, whichever executor produced it.</summary>
/// <remarks>
/// The limit counts UTF-8 bytes and never cuts a character in half: a cut that
/// split a surrogate pair would put an invalid character in front of the model.
/// </remarks>
internal static class SkillScriptOutput
{
    /// <summary>Returns <paramref name="text"/> cut to at most <paramref name="maxBytes"/> UTF-8 bytes.</summary>
    /// <param name="text">The output text.</param>
    /// <param name="maxBytes">The byte limit.</param>
    /// <param name="truncated">Set to <see langword="true"/> when the text was cut; left unchanged otherwise.</param>
    /// <returns>The text, or its longest prefix that fits.</returns>
    public static string Cap(string text, int maxBytes, ref bool truncated)
    {
        if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
        {
            return text;
        }

        truncated = true;

        return text[..FittingLength(text.AsSpan(), maxBytes)];
    }

    /// <summary>The number of leading characters of <paramref name="text"/> that fit in <paramref name="maxBytes"/> UTF-8 bytes.</summary>
    /// <param name="text">The characters.</param>
    /// <param name="maxBytes">The byte budget; zero or less keeps nothing.</param>
    /// <returns>A character count that never ends inside a surrogate pair.</returns>
    public static int FittingLength(ReadOnlySpan<char> text, int maxBytes)
    {
        var used = 0;
        var index = 0;

        while (index < text.Length)
        {
            var width = char.IsHighSurrogate(text[index])
                && index + 1 < text.Length
                && char.IsLowSurrogate(text[index + 1])
                    ? 2
                    : 1;

            var bytes = Encoding.UTF8.GetByteCount(text.Slice(index, width));

            if (used + bytes > maxBytes)
            {
                break;
            }

            used += bytes;
            index += width;
        }

        return index;
    }
}
