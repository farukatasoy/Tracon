using System.Security.Cryptography;
using System.Text;

namespace Tracon;

/// <summary>
/// Makes a string safe to bind to a text or JSON parameter of any SQL store.
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL cannot hold U+0000: a <c>text</c> value that contains it fails with
/// <c>22021</c>, and a <c>jsonb</c> value that contains the escape <c>\u0000</c> fails with
/// <c>22P05</c>. SQL Server and SQLite accept it. A model reply, a tool result, or a request
/// body can carry one, and the write then failed on one store and not on the others.
/// </para>
/// <para>
/// Every SQL store therefore writes U+FFFD in its place, so the four stores accept the same
/// values. The in-memory stores keep the value as it came.
/// </para>
/// </remarks>
internal static class SqlText
{
    private const char Replacement = '�';

    /// <summary>Replaces each U+0000 of a text value.</summary>
    /// <param name="value">The text; it can be <see langword="null"/>.</param>
    /// <returns>The text with U+FFFD where U+0000 stood; the same instance when it held none.</returns>
    public static string? WithoutNul(string? value)
        => value is null || value.IndexOf('\0', StringComparison.Ordinal) < 0 ? value : value.Replace('\0', Replacement);

    /// <summary>The fewest characters a bound may leave for <see cref="Fit"/> to keep something of the text.</summary>
    private const int MinimumFitLimit = 64;

    /// <summary>How many hexadecimal digits of the text's SHA-256 <see cref="Fit"/> writes after the part it keeps.</summary>
    private const int FitHashDigits = 32;

    /// <summary>
    /// Shortens a label that is longer than the column that holds it to a form that still names it: the start of the text,
    /// then <c>~</c>, then the start of the text's SHA-256.
    /// </summary>
    /// <param name="value">The label; it can be <see langword="null"/>.</param>
    /// <param name="limit">The longest text the column holds, in UTF-16 code units.</param>
    /// <returns>
    /// The label as it is when it fits; otherwise a text of exactly <paramref name="limit"/> characters (one fewer when
    /// the cut would have split a surrogate pair).
    /// </returns>
    /// <remarks>
    /// <para>
    /// For a label the store must not lose: an audit entry names an actor, an action and an entity, and a store that
    /// refuses the entry because one of them is longer than its column loses a record of something that happened. The kept
    /// start says what kind of thing it was (<c>session:</c>, <c>tool:</c>) and most of its name; the digits tell two
    /// labels that share their first 160 characters apart, and a reader holding the whole label finds its row by
    /// shortening it the same way.
    /// </para>
    /// <para>
    /// The text is cut at a UTF-16 code unit, because that is what a SQL Server <c>nvarchar</c> column counts, but never
    /// between the two halves of a surrogate pair: a lone half does not survive the round trip through the database.
    /// </para>
    /// </remarks>
    public static string? Fit(string? value, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, MinimumFitLimit);

        if (value is null || value.Length <= limit)
        {
            return value;
        }

        var digits = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..FitHashDigits];
        var keep = limit - 1 - FitHashDigits;

        if (char.IsHighSurrogate(value[keep - 1]))
        {
            keep--;
        }

        return string.Concat(value.AsSpan(0, keep), "~", digits);
    }

    /// <summary>Gives a currency label the one form every SQL store holds: upper case, in the invariant culture.</summary>
    /// <param name="currency">The label as the caller wrote it; it can be <see langword="null"/>.</param>
    /// <returns>The label in upper case; <see langword="null"/> stays <see langword="null"/>.</returns>
    /// <remarks>
    /// The cost queries upper-case the column in SQL (<c>UPPER</c>) so that <c>usd</c> and <c>USD</c> are one
    /// currency. That function follows the locale of the database: under a Turkish locale
    /// <c>UPPER('inr')</c> gives a dotted capital I (U+0130) before <c>NR</c>, while <c>INR</c> stays <c>INR</c>, so one currency became two totals and
    /// the amount was left out. A label stored in upper case reaches <c>UPPER</c> unchanged in every locale.
    /// </remarks>
    public static string? Currency(string? currency)
        => currency?.ToUpperInvariant();

    /// <summary>Replaces each U+0000 of a JSON text, written raw or as the escape <c>\u0000</c>.</summary>
    /// <param name="json">The JSON text; it can be <see langword="null"/>.</param>
    /// <returns>The JSON text with U+FFFD where U+0000 stood; the same instance when it held none.</returns>
    /// <remarks>
    /// The escape is found by reading the text one escape at a time, so the characters
    /// <c>\\u0000</c> (a backslash, then the five characters <c>u0000</c>) stay as they are.
    /// </remarks>
    public static string? InJson(string? json)
    {
        json = WithoutNul(json);

        if (json is null || !json.Contains("u0000", StringComparison.Ordinal))
        {
            return json;
        }

        var builder = new StringBuilder(json.Length);

        for (var index = 0; index < json.Length; index++)
        {
            var current = json[index];

            if (current != '\\' || index + 1 >= json.Length)
            {
                builder.Append(current);

                continue;
            }

            if (json[index + 1] == 'u' && index + 5 < json.Length && json.AsSpan(index + 2, 4).SequenceEqual("0000"))
            {
                builder.Append("\\uFFFD");
                index += 5;

                continue;
            }

            builder.Append(current).Append(json[index + 1]);
            index++;
        }

        return builder.ToString();
    }
}
