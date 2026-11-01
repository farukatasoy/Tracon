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
