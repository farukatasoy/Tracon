using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CsCheck;

namespace Tracon.Tests.Differential;

/// <summary>
/// The generators of boundary values, written once and used by every property and every state machine.
/// </summary>
/// <remarks>
/// <para>
/// Text is built from a list of atoms that have broken a comparison, a key or a column somewhere: letters whose case
/// folding differs under a Turkish culture, characters that fold to a different length, a surrogate pair, a base
/// letter followed by a combining mark (and the same letter precomposed), U+0000, zero-width and bidirectional
/// controls, characters that are special in SQL patterns and in JSON. Only valid UTF-16 is produced: every input
/// reaches a store through a UTF-8 decoder, which never yields a lone surrogate.
/// </para>
/// <para>
/// A key pool also holds <em>twins</em>: a spelling that differs from another key only in the way a lenient comparison
/// would ignore (letter case, a Turkish dotted or dotless i, a decomposed accent, a trailing space, U+0000 against
/// U+FFFD). A pool with twins is what makes two keys collide often enough to be tested.
/// </para>
/// </remarks>
internal static class BoundaryValues
{
    private static readonly string[] Atoms =
    [
        "a", "A", "z", "Z", "0", "9", "-", "_", ".", "/", ":", ";", "?", "*", "@", "$", "#", "&", "=", "+", "~", "!",
        "%", "[", "]", "'", "\"", "\\", "`", "{", "}", "--", "\\u0000", "\\\\", "%00", "\\n",
        " ", "  ", "\t", "\n", "\r\n", "\u0001", "\u001f", "\u007f", "\0", "\uFFFD",
        "i", "I", "\u0130", "\u0131", "\u00DF", "\u1E9E", "\u01C5", "\u03A3", "\u03C3", "\u03C2", "\u212A", "\u017F", "\u212B", "\uFB03",
        "\u00E9", "e\u0301", "\u00C9", "E\u0301", "\u00C5", "A\u030A",
        "\U0001F600", "\U0001F468\u200D\U0001F469\u200D\U0001F467", "\U0001D11E", "\U00010400",
        "\u200B", "\u200D", "\uFEFF", "\u2028", "\u202E", "\u00A0", "\u3000", "\u6F22\u5B57", "\u0627\u0644\u0639\u0631\u0628\u064A\u0629",
        "\uD55C\uAE00", "\u0E01\u0E32",
    ];

    private const string TenantCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_.-";

    private static readonly TimeSpan[] Offsets =
    [
        TimeSpan.FromHours(14),
        TimeSpan.FromHours(-12),
        TimeSpan.FromMinutes(330),
        TimeSpan.FromMinutes(345),
        TimeSpan.FromMinutes(-210),
        TimeSpan.FromHours(3),
    ];

    private static readonly DateTimeOffset Baseline = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset[] InstantEdges =
    [
        DateTimeOffset.MinValue,
        DateTimeOffset.MinValue.AddTicks(1),
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch.AddTicks(-1),
        DateTimeOffset.UnixEpoch.AddTicks(9),
        new DateTimeOffset(2038, 1, 19, 3, 14, 7, TimeSpan.Zero),
        new DateTimeOffset(2038, 1, 19, 3, 14, 8, TimeSpan.Zero),
        new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9_999_999),
        new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero),
    ];

    /// <summary>Gets a short word of letters, digits and two separators.</summary>
    public static Gen<string> Word { get; } = Gen.String[Gen.Char["abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_"], 1, 10];

    /// <summary>Gets one atom: a short string that has broken a comparison somewhere.</summary>
    public static Gen<string> OneAtom { get; } = Gen.OneOf(Atoms);

    /// <summary>
    /// Generates a non-empty text no longer than <paramref name="maxLength"/> UTF-16 code units, built from atoms and
    /// from plain words, now and then filled to the length limit or one short of it.
    /// </summary>
    /// <param name="maxLength">The longest text, in UTF-16 code units.</param>
    /// <returns>The generator.</returns>
    public static Gen<string> Text(int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        var composite = Gen.Int[1, 4].SelectMany(count => Gen.OneOf(Word, OneAtom).Array[count]).Select(parts => Fit(string.Concat(parts), maxLength));
        var atoms = OneAtom.Array[1, 3].Select(parts => Fit(string.Concat(parts), maxLength));
        var filled = Gen.Int[0, 2].Select(back => Filled(maxLength - back));
        var multiByteFilled = Gen.Int[0, 1].Select(back => Fit(string.Concat(Enumerable.Repeat("\U0001F600", maxLength)), maxLength - back));

        return Gen.Frequency(
            (6, Word.Select(word => Fit(word, maxLength))),
            (9, composite),
            (6, atoms),
            (1, filled),
            (1, multiByteFilled));
    }

    /// <summary>
    /// Generates a tenant identifier of the form the HTTP surface accepts: one to 64 ASCII letters, digits, underscores,
    /// dots and hyphens, now and then at the longest length.
    /// </summary>
    public static Gen<string> TenantId { get; } = Gen.Frequency(
        (8, Gen.String[Gen.Char[TenantCharacters], 1, 8]),
        (3, Gen.String[Gen.Char[TenantCharacters], 1, 64]),
        (1, Gen.Const(new string('A', 64))),
        (1, Gen.Const(new string('a', 63) + "Z")));

    /// <summary>Generates a pool of tenant identifiers in which some are the letter-case twin of another.</summary>
    /// <param name="count">The size of the pool.</param>
    /// <returns>The generator.</returns>
    public static Gen<string[]> TenantPool(int count)
        => Pool(
            count,
            TenantId,
            static tenant =>
            {
                var candidates = new[] { tenant.ToUpperInvariant(), tenant.ToLowerInvariant(), Swap(tenant) }
                    .Where(candidate => !string.Equals(candidate, tenant, StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                return candidates.Length == 0 ? Gen.Const(tenant) : Gen.OneOf(candidates);
            });

    /// <summary>
    /// Generates a tenant spelling that is never blank: a text where a blank result is prefixed with a letter, so that
    /// leading white space and the other awkward characters stay.
    /// </summary>
    /// <param name="maxLength">The longest text, in UTF-16 code units.</param>
    /// <returns>The generator.</returns>
    public static Gen<string> NonBlank(int maxLength)
        => Text(maxLength).Select(text => string.IsNullOrWhiteSpace(text) ? Fit("t" + text, maxLength) : text);

    /// <summary>Generates a text that is never blank and has no leading or trailing white space.</summary>
    /// <param name="maxLength">The longest text, in UTF-16 code units.</param>
    /// <returns>The generator.</returns>
    public static Gen<string> Trimmed(int maxLength)
        => Text(maxLength)
            .Select(static text => text.Trim(' ', '\t', '\r', '\n', '\u00A0', '\u3000', '\u2028'))
            .Select(text => text.Length == 0 ? "x" : text)
            .Select(text => Fit(text, maxLength))
            .Select(static text => string.IsNullOrWhiteSpace(text) ? "x" : text);

    /// <summary>
    /// Returns a spelling that differs from <paramref name="key"/> only in a way a lenient comparison ignores, or
    /// <paramref name="key"/> itself when none applies.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="maxLength">The longest allowed spelling.</param>
    /// <param name="trailingSpace">Whether a trailing space counts as a twin (a column that pads when it compares).</param>
    /// <returns>The generator.</returns>
    public static Gen<string> Twin(string key, int maxLength, bool trailingSpace = false)
    {
        ArgumentNullException.ThrowIfNull(key);

        var candidates = new List<string>
        {
            key.ToUpperInvariant(),
            key.ToLowerInvariant(),
            Swap(key),
            key.Replace('i', '\u0130').Replace('I', '\u0131'),
            key.Replace('\u0130', 'i').Replace('\u0131', 'I'),
            key.Replace('k', '\u212A'),
            key.Replace('s', '\u017F'),
        };

        if (key.IsNormalized(NormalizationForm.FormC))
        {
            candidates.Add(key.Normalize(NormalizationForm.FormD));
        }
        else
        {
            candidates.Add(key.Normalize(NormalizationForm.FormC));
        }

        if (key.Contains('\0', StringComparison.Ordinal))
        {
            candidates.Add(key.Replace('\0', '\uFFFD'));
        }

        if (key.Contains('\uFFFD', StringComparison.Ordinal))
        {
            candidates.Add(key.Replace('\uFFFD', '\0'));
        }

        if (trailingSpace)
        {
            candidates.Add(key + " ");
        }

        var distinct = candidates
            .Where(candidate => candidate.Length <= maxLength && !string.Equals(candidate, key, StringComparison.Ordinal) && candidate.Trim().Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return distinct.Length == 0 ? Gen.Const(key) : Gen.OneOf(distinct);
    }

    /// <summary>
    /// Generates a pool of keys in which each new key is either unrelated to the earlier ones or a twin of one.
    /// </summary>
    /// <param name="count">The size of the pool.</param>
    /// <param name="fresh">The generator of an unrelated key.</param>
    /// <param name="maxLength">The longest key.</param>
    /// <param name="trailingSpace">Whether a trailing space counts as a twin.</param>
    /// <returns>The generator.</returns>
    public static Gen<string[]> Pool(int count, Gen<string> fresh, int maxLength, bool trailingSpace = false)
        => Pool(count, fresh, key => Twin(key, maxLength, trailingSpace));

    /// <summary>
    /// Generates a pool of keys in which each new key is either unrelated to the earlier ones or the twin of one that
    /// <paramref name="twin"/> produces.
    /// </summary>
    /// <param name="count">The size of the pool.</param>
    /// <param name="fresh">The generator of an unrelated key.</param>
    /// <param name="twin">Produces a twin of a key.</param>
    /// <returns>The generator.</returns>
    public static Gen<string[]> Pool(int count, Gen<string> fresh, Func<string, Gen<string>> twin)
    {
        ArgumentNullException.ThrowIfNull(fresh);
        ArgumentNullException.ThrowIfNull(twin);

        var pool = Gen.Const(Array.Empty<string>());

        for (var index = 0; index < count; index++)
        {
            pool = pool.SelectMany(
                current => current.Length == 0
                    ? fresh
                    : Gen.Frequency(
                        (1, fresh),
                        (2, Gen.OneOf(current).SelectMany(twin))),
                static (current, next) => current.Append(next).ToArray());
        }

        return pool;
    }

    /// <summary>
    /// Generates an instant: usually close to a fixed day, now and then a sub-microsecond tick, a non-UTC offset, or
    /// the end of the representable range.
    /// </summary>
    public static Gen<DateTimeOffset> Instant { get; } = Gen.Frequency(
        (10, Gen.Int[0, 3_000_000].Select(static ms => Baseline.AddMilliseconds(ms))),
        (3, Gen.Int[-3, 12].Select(static ticks => Baseline.AddTicks(ticks))),
        (2, Gen.Int[0, 5].SelectMany(static index => Gen.Int[0, 100_000].Select(ms => Baseline.AddMilliseconds(ms).ToOffset(Offsets[index])))),
        (1, Gen.OneOf(InstantEdges)));

    /// <summary>Generates an instant that the store keeps apart from every other one: a whole millisecond on a fixed day.</summary>
    public static Gen<DateTimeOffset> PlainInstant { get; } = Gen.Int[0, 3_000_000].Select(static ms => Baseline.AddMilliseconds(ms));

    /// <summary>Generates a cost: zero, whole cents, ten decimal places, a value past the stored precision, and large sums.</summary>
    public static Gen<decimal> Cost { get; } = Gen.Frequency(
        (3, Gen.OneOf(0m, 0.01m, 0.0000000001m, 0.00000000005m, 0.1234567890m, 12345.678901m, 1m, 99.99m)),
        (4, Gen.Int[0, 1_000_000].Select(static cents => cents / 100m)),
        (2, Gen.Int[1, 9_999_999].Select(static units => units / 10_000_000_000m)),
        (1, Gen.OneOf(9_999_999_999.9999999999m, 999_999_999.999999m, 0.0000000000999m)));

    /// <summary>
    /// Generates the text of a JSON document whose root is an object or an array, with strings from the boundary list.
    /// </summary>
    public static Gen<string> JsonDocument { get; } = JsonValueGen(2).Select(static node =>
        node is JsonObject or JsonArray ? node.ToJsonString() : new JsonObject { ["value"] = node }.ToJsonString());

    /// <summary>Renders a string so that every character that is not printable ASCII is visible.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The quoted, escaped text.</returns>
    public static string Show(string? value)
    {
        if (value is null)
        {
            return "null";
        }

        var builder = new StringBuilder(value.Length + 2).Append('"');

        foreach (var character in value)
        {
            if (character == '"')
            {
                builder.Append("\\\"");
            }
            else if (character == '\\')
            {
                builder.Append("\\\\");
            }
            else if (character is >= ' ' and <= '~')
            {
                builder.Append(character);
            }
            else
            {
                builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            }
        }

        return builder.Append('"').ToString();
    }

    /// <summary>Renders an instant as its UTC ticks and as a readable UTC time.</summary>
    /// <param name="instant">The instant.</param>
    /// <returns>The text.</returns>
    public static string Show(DateTimeOffset instant)
        => $"{instant.UtcTicks}t({instant.ToString("O", CultureInfo.InvariantCulture)})";


    private static Gen<JsonNode> JsonValueGen(int depth)
    {
        var leaf = Gen.Frequency(
            (5, Text(24).Select(static text => (JsonNode)JsonValue.Create(text)!)),
            (2, Gen.Int[-1_000_000, 1_000_000].Select(static number => (JsonNode)JsonValue.Create(number)!)),
            (1, Gen.OneOf(0.5m, -12.25m, 3.14159m, 100m, 0.001m).Select(static number => (JsonNode)JsonValue.Create(number)!)),
            (1, Gen.Bool.Select(static flag => (JsonNode)JsonValue.Create(flag)!)),
            (1, Gen.Const(() => (JsonNode)JsonValue.Create(9_007_199_254_740_993L)!)));

        if (depth == 0)
        {
            return leaf;
        }

        var inner = JsonValueGen(depth - 1);
        var key = Gen.String[Gen.Char["abcdefgh_ \u00E9\u0130"], 1, 6];
        var obj = Gen.Int[0, 3].SelectMany(count =>
            key.Array[count].SelectMany(keys => inner.Array[count].Select(values =>
            {
                var result = new JsonObject();

                for (var index = 0; index < keys.Length; index++)
                {
                    result[keys[index]] = values[index].DeepClone();
                }

                return (JsonNode)result;
            })));
        var array = inner.Array[0, 3].Select(static values =>
        {
            var result = new JsonArray();

            foreach (var value in values)
            {
                result.Add(value.DeepClone());
            }

            return (JsonNode)result;
        });

        return Gen.Frequency((4, leaf), (3, obj), (2, array));
    }

    private static string Filled(int length)
        => new('x', Math.Max(length, 1));

    private static string Fit(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var cut = maxLength;

        // Never end on the first half of a surrogate pair.
        if (char.IsHighSurrogate(value[cut - 1]))
        {
            cut--;
        }

        return cut == 0 ? "x" : value[..cut];
    }

    private static string Swap(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            builder.Append(char.IsUpper(character) ? char.ToLowerInvariant(character) : char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }
}
