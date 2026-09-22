using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// The deterministic fingerprint of an approval rule's argument-condition set:
/// the uniqueness key every <see cref="IToolApprovalRuleStore"/> in Tracon compares.
/// </summary>
/// <remarks>
/// <para>
/// One function for every store. The SQL stores persist it as
/// <c>conditions_hash</c> and deduplicate on it; the in-memory store compares it
/// directly. When each store carried its own copy, the copies disagreed in both
/// directions: the in-memory store kept <c>["eu", "us"]</c> and
/// <c>["eu","us"]</c> as two rules while the SQL stores kept one, and the SQL
/// stores merged two different sets that the in-memory store kept apart (see the
/// format note below).
/// </para>
/// <para>
/// <strong>Format.</strong> Conditions are sorted first, so the same SET hashes
/// the same in any order. Each condition is then written as its path, operator
/// number, and canonical JSON value, joined with the unit separator (U+001F) and
/// ended with the record separator (U+001E). That is the format existing
/// <c>conditions_hash</c> rows were written in, and it is kept for every set it
/// can represent. It can represent a set only when no path carries either
/// separator: the operator is a number, and a canonical JSON value never
/// carries a raw control character (JSON escapes them). A path is free text,
/// though — a JSON property name may hold any character — so a path that
/// carries a separator could read as a field boundary, and
/// <c>{"a\u001F0\u001F1\u001Eb": 2}</c> hashed the same as
/// <c>{"a": 1, "b": 2}</c>. A set with such a path is written length-prefixed
/// instead, after a leading record separator that no separator-joined text can
/// start with. Existing rows keep their hash; no migration is needed.
/// </para>
/// </remarks>
internal static class ToolArgumentConditionFingerprint
{
    private const char UnitSeparator = '\u001F';
    private const char RecordSeparator = '\u001E';

    /// <summary>Computes the fingerprint of a condition set.</summary>
    /// <param name="conditions">The conditions, in any order.</param>
    /// <returns>
    /// The uppercase hexadecimal SHA-256 of the canonical text;
    /// <see langword="null"/> for an empty set, symmetric with
    /// <see cref="ToolApprovalRule.ArgumentsHash"/>'s "no constraint" meaning.
    /// </returns>
    public static string? Compute(IReadOnlyList<ToolArgumentCondition> conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);

        if (conditions.Count == 0)
        {
            return null;
        }

        var ordered = conditions
            .Select(static condition => (condition.Path, Operator: (int)condition.Operator, Text: CanonicalizeJson(condition.Value)))
            .OrderBy(static entry => entry.Path, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Operator)
            .ThenBy(static entry => entry.Text, StringComparer.Ordinal)
            .ToList();

        var builder = new StringBuilder();

        if (ordered.Exists(static entry => entry.Path.AsSpan().IndexOfAny(UnitSeparator, RecordSeparator) >= 0))
        {
            builder.Append(RecordSeparator);

            foreach (var entry in ordered)
            {
                AppendLengthPrefixed(builder, entry.Path);
                AppendLengthPrefixed(builder, entry.Operator.ToString(CultureInfo.InvariantCulture));
                AppendLengthPrefixed(builder, entry.Text);
            }
        }
        else
        {
            foreach (var entry in ordered)
            {
                builder.Append(entry.Path)
                    .Append(UnitSeparator)
                    .Append(entry.Operator.ToString(CultureInfo.InvariantCulture))
                    .Append(UnitSeparator)
                    .Append(entry.Text)
                    .Append(RecordSeparator);
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    /// <summary>Renders a JSON value with no incidental whitespace, so equal values always hash the same.</summary>
    /// <param name="value">The condition value.</param>
    /// <returns>The canonical text.</returns>
    private static string CanonicalizeJson(JsonElement value)
        => value.ValueKind == JsonValueKind.Array
            ? "[" + string.Join(",", value.EnumerateArray().Select(CanonicalizeJson)) + "]"
            : value.GetRawText();

    /// <summary>Writes one field as its UTF-8 byte length, a colon, then the value.</summary>
    /// <param name="builder">The buffer being hashed.</param>
    /// <param name="value">The field.</param>
    private static void AppendLengthPrefixed(StringBuilder builder, string value)
        => builder.Append(Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value);
}
