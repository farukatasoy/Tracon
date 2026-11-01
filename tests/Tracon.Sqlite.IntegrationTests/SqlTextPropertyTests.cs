using System.Text.Json;
using CsCheck;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// Properties of <see cref="SqlText"/> over generated strings: the JSON it rewrites is still the same JSON, with each U+0000
/// replaced and nothing else touched.
/// </summary>
/// <remarks>
/// The JSON serializer is the oracle. A string is serialized (so a NUL becomes the escape <c>\u0000</c>, a backslash becomes
/// <c>\\</c>, and a backslash followed by the characters <c>u0000</c> becomes <c>\\u0000</c>), rewritten by
/// <see cref="SqlText.InJson"/>, and read back; the result must be the string with U+0000 replaced by U+FFFD. The generator
/// leans on the characters that make the escape reading hard: backslashes, the letter u, zeros, quotes and NUL.
/// </remarks>
public sealed class SqlTextPropertyTests
{
    // Valid UTF-16 only: a lone surrogate is turned into U+FFFD by the serializer itself, which is not what is measured.
    private static readonly Gen<string> Text = Gen.OneOf(
        Gen.String[Gen.Char["\\u0\"a\0\n"], 0, 14],
        Gen.OneOf<string>("\\u0000", "\\\\u0000", "\\\\\\u0000", "u0000", "\0", "a\0b\\u0000c", "\\", "\U0001F600", "\U0001F600\0"),
        Gen.String[Gen.Char["abcXYZ 09-_/."], 0, 20]);

    [Fact]
    public void A_json_string_keeps_its_value_except_that_each_nul_is_replaced()
    {
        Text.Sample(
            static value =>
            {
                var json = JsonSerializer.Serialize(value);
                var rewritten = SqlText.InJson(json);

                var read = JsonSerializer.Deserialize<string>(rewritten!);

                read.ShouldBe(value.Replace('\0', '\uFFFD'), $"value {JsonSerializer.Serialize(value)}");
            },
            iter: 10_000);
    }

    [Fact]
    public void A_json_array_of_strings_keeps_every_element()
    {
        Text.Array[0, 6].Sample(
            static values =>
            {
                var json = JsonSerializer.Serialize(values);
                var rewritten = SqlText.InJson(json);

                JsonSerializer.Deserialize<string[]>(rewritten!).ShouldBe(values.Select(static v => v.Replace('\0', '\uFFFD')).ToArray());
            },
            iter: 5_000);
    }

    [Fact]
    public void A_text_without_a_nul_is_never_rewritten()
    {
        Text.Where(static value => !value.Contains('\0', StringComparison.Ordinal)).Sample(
            static value =>
            {
                SqlText.WithoutNul(value).ShouldBe(value);

                var json = JsonSerializer.Serialize(value);

                // The serializer escapes a NUL only as \u0000; a value without one has no such escape to find,
                // although it may contain a backslash, the letter u and zeros that spell one.
                JsonSerializer.Deserialize<string>(SqlText.InJson(json)!).ShouldBe(value);
            },
            iter: 5_000);
    }
}
