using System.Security.Cryptography;
using System.Text;

namespace Tracon;

/// <summary>
/// Shortens a label that is longer than the column that holds it to a form that still names it: the start of the text,
/// then <c>~</c>, then the start of the text's SHA-256.
/// </summary>
/// <remarks>
/// <para>
/// For a label the store must not lose: an audit entry names an actor, an action and an entity, and a store that refuses the
/// entry because one of them is longer than its column loses a record of something that happened. The kept start says what
/// kind of thing it was (<c>session:</c>, <c>tool:</c>) and most of its name; the digits tell two labels that share their
/// first 160 characters apart, and a reader holding the whole label finds its row by shortening it the same way.
/// </para>
/// <para>
/// The text is cut at a UTF-16 code unit, because that is what a SQL Server <c>nvarchar</c> column counts, but never between
/// the two halves of a surrogate pair: a lone half does not survive the round trip through the database.
/// </para>
/// <para>
/// One copy: the SQL stores shorten what they bind with it (<c>SqlText.Fit</c>), and the actor resolver shortens a caller's
/// name with it before any store sees the name.
/// </para>
/// </remarks>
internal static class LabelFit
{
    /// <summary>The fewest characters a bound may leave for <see cref="Fit"/> to keep something of the text.</summary>
    private const int MinimumLimit = 64;

    /// <summary>How many hexadecimal digits of the text's SHA-256 <see cref="Fit"/> writes after the part it keeps.</summary>
    private const int HashDigits = 32;

    /// <summary>Shortens a label to at most <paramref name="limit"/> characters.</summary>
    /// <param name="value">The label; it can be <see langword="null"/>.</param>
    /// <param name="limit">The longest text the column holds, in UTF-16 code units.</param>
    /// <returns>
    /// The label as it is when it fits; otherwise a text of exactly <paramref name="limit"/> characters (one fewer when the
    /// cut would have split a surrogate pair).
    /// </returns>
    public static string? Fit(string? value, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, MinimumLimit);

        if (value is null || value.Length <= limit)
        {
            return value;
        }

        var digits = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..HashDigits];
        var keep = limit - 1 - HashDigits;

        if (char.IsHighSurrogate(value[keep - 1]))
        {
            keep--;
        }

        return string.Concat(value.AsSpan(0, keep), "~", digits);
    }
}
