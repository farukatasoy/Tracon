namespace Tracon;

/// <summary>
/// Cuts text to a length without splitting a surrogate pair.
/// </summary>
/// <remarks>
/// <para>
/// A character above U+FFFF (an emoji, a rare CJK character) is two UTF-16 characters. A cut by
/// character count that lands between them leaves a lone high surrogate at the end of the kept
/// text. It is not text: Npgsql refuses to encode it, <c>System.Text.Json</c> refuses to write it,
/// and the other engines store a replacement character. The cut text then fails where it is
/// stored or serialised, far from the cut.
/// </para>
/// <para>
/// This type is the ONE place a stored or emitted text is cut by count. A cut written by hand
/// needs the same guard every time, and a copy without it fails far from the cut, so no other
/// code cuts such text by hand.
/// </para>
/// </remarks>
internal static class SurrogateSafeText
{
    /// <summary>Gets how many characters of <paramref name="value"/> to keep so the kept text ends on a whole character.</summary>
    /// <param name="value">The text.</param>
    /// <param name="maxLength">The most characters to keep. A negative value keeps none.</param>
    /// <returns>
    /// <paramref name="maxLength"/>, or one less when the cut would split a surrogate pair; the
    /// length of <paramref name="value"/> when it already fits.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static int PrefixLength(string value, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (maxLength >= value.Length)
        {
            return value.Length;
        }

        if (maxLength <= 0)
        {
            return 0;
        }

        // A high surrogate as the last kept character has its low half among the dropped
        // ones (or has none, and was never text): either way it does not stay.
        return char.IsHighSurrogate(value[maxLength - 1]) ? maxLength - 1 : maxLength;
    }

    /// <summary>Keeps at most <paramref name="maxLength"/> characters of <paramref name="value"/>, never splitting a surrogate pair.</summary>
    /// <param name="value">The text.</param>
    /// <param name="maxLength">The most characters to keep.</param>
    /// <returns>The same instance when it fits; otherwise the kept prefix.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static string Prefix(string value, int maxLength)
    {
        var length = PrefixLength(value, maxLength);

        return length == value.Length ? value : value[..length];
    }
}
