using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tracon.Tests.Differential;

/// <summary>
/// The tenant namespace of one generated sequence, so that sequences never see each other's rows and the database
/// needs no reset between them.
/// </summary>
/// <remarks>
/// <para>
/// Every tenant a step names is written with a prefix that is the same under any letter-case folding and is unique to
/// the sequence (including each replay while a failing sequence shrinks). The model works on the unprefixed spelling;
/// the driver adds the prefix on the way in and removes it from the records it renders.
/// </para>
/// <para>
/// A blank tenant is passed through unchanged, because the product rejects it before any row is touched and the
/// differential must see that rejection, not a prefix that makes the value valid.
/// </para>
/// </remarks>
internal sealed class DifferentialScope
{
    private static readonly string Run = Guid.NewGuid().ToString("N")[..8];

    private static int _counter;

    private DifferentialScope(string prefix) => Prefix = prefix;

    /// <summary>Gets the length every stored tenant column allows.</summary>
    public const int ColumnLength = 200;

    /// <summary>Gets the prefix that makes the sequence's tenants its own.</summary>
    public string Prefix { get; }

    /// <summary>Gets the longest tenant spelling a step can use once the prefix is added.</summary>
    public static int MaxTenantLength => ColumnLength - NewPrefixLength();

    /// <summary>Starts the namespace of a new sequence.</summary>
    /// <returns>The scope.</returns>
    public static DifferentialScope Next()
        => new("d" + Run + Interlocked.Increment(ref _counter).ToString("x6", CultureInfo.InvariantCulture) + "-");

    /// <summary>Adds the prefix to a tenant spelling.</summary>
    /// <param name="tenant">The spelling a step names.</param>
    /// <returns>The spelling the store receives.</returns>
    public string Wrap(string? tenant)
        => tenant is null || string.IsNullOrWhiteSpace(tenant) ? tenant! : Prefix + tenant;

    /// <summary>Removes the prefix from a tenant a store returned.</summary>
    /// <param name="tenant">The tenant as the store holds it.</param>
    /// <returns>The tenant without the sequence prefix; unchanged when the prefix is not there.</returns>
    public string? Unwrap(string? tenant)
        => tenant is not null && tenant.StartsWith(Prefix, StringComparison.Ordinal) ? tenant[Prefix.Length..] : tenant;

    private static int NewPrefixLength() => 1 + Run.Length + 6 + 1;
}

/// <summary>Reads a JSON document into one canonical text, so two documents compare by value.</summary>
internal static class CanonicalJson
{
    /// <summary>Gets the canonical text of a JSON document.</summary>
    /// <param name="json">The JSON text.</param>
    /// <param name="text">The rule that maps every string and key (U+0000 handling); <see langword="null"/> keeps them.</param>
    /// <returns>The text: members in ordinal order, numbers in one form, no insignificant white space.</returns>
    public static string Of(string json, Func<string, string>? text = null)
    {
        ArgumentNullException.ThrowIfNull(json);

        var node = JsonNode.Parse(json);

        return Render(node, text ?? (static value => value));
    }

    private static string Render(JsonNode? node, Func<string, string> text)
    {
        switch (node)
        {
            case null:
                return "null";
            case JsonObject obj:
                return "{" + string.Join(
                    ",",
                    obj.Select(pair => (Key: text(pair.Key), pair.Value))
                        .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => JsonSerializer.Serialize(pair.Key) + ":" + Render(pair.Value, text))) + "}";
            case JsonArray array:
                return "[" + string.Join(",", array.Select(item => Render(item, text))) + "]";
            case JsonValue value when value.TryGetValue<string>(out var s):
                return JsonSerializer.Serialize(text(s));
            case JsonValue value when value.TryGetValue<bool>(out var b):
                return b ? "true" : "false";
            default:
                var raw = node.ToJsonString();

                return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    ? number.ToString("0.############################", CultureInfo.InvariantCulture)
                    : raw;
        }
    }
}
