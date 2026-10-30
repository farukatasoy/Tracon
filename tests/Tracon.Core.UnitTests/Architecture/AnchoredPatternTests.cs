using System.Text.RegularExpressions;
using Tracon.Tests.Common;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every pattern in <c>src/</c> that validates a whole value ends in <c>\z</c>, never in <c>$</c>.
/// </summary>
/// <remarks>
/// <para>
/// In .NET <c>$</c> matches at the very end of the text AND before a trailing <c>\n</c>, so
/// <c>^[a-z0-9]+$</c> accepts <c>"acme\n"</c>. Seven validators wrote it that way: a tenant id, a custom event type,
/// a job handler key, a job lane, a knowledge collection, an MCP name and an agent parameter name. A claim an identity
/// provider issued, a route segment or a stored definition can carry the line feed (a header cannot), and the value then
/// became a name no request could ever write again.
/// </para>
/// <para>
/// The gate reads the source, because the pattern literal is the only place the pattern is written once. It looks at
/// every way the product writes a pattern with a literal: the <c>[GeneratedRegex]</c> attribute, <c>new Regex("…")</c>,
/// a field or local declared <c>Regex name = new("…")</c> and the static <c>Regex.IsMatch / Match / Matches / Replace /
/// Split</c> calls that take the pattern as a second argument. A pattern that starts with <c>^</c> is a whole-value
/// validator and must end with <c>\z</c>. Patterns that look INSIDE a text (a content matcher) do not start with
/// <c>^</c> and are out of scope; a pattern that arrives in a variable (a consumer's schema) cannot be read here.
/// </para>
/// </remarks>
public sealed class AnchoredPatternTests
{
    // A string literal as written in source: a verbatim @"..." (a doubled quote is a quote) or a regular "..."
    // (a backslash escapes the next character). The raw string form is not used by any pattern.
    private const string Literal = """(?:@"(?<verbatim>(?:[^"]|"")*)"|"(?<regular>(?:[^"\\]|\\.)*)")""";

    private static readonly Regex Site = new(
        """(?:\[GeneratedRegex\(|new Regex\(|\bRegex\s+\w+\s*=\s*new\(|\bRegex\.(?:IsMatch|Match|Matches|Replace|Split)\([^"\n]*?,)\s*""" + Literal,
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void Every_whole_value_pattern_ends_in_the_absolute_end_of_the_text()
    {
        var violations = new List<string>();
        var anchored = 0;

        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);

            foreach (var found in Scan(text))
            {
                if (!found.StartsAnchored)
                {
                    continue;
                }

                anchored++;

                if (!found.EndsAbsolute)
                {
                    violations.Add($"{Path.GetRelativePath(RepoRoot.Path, file)}:{found.Line}: {found.Pattern}");
                }
            }
        }

        // A scan that found nothing would pass for the wrong reason. 2026-10-29: eight whole-value patterns (the
        // seven of an earlier fix and the audit chain key id).
        anchored.ShouldBeGreaterThanOrEqualTo(8);

        violations.ShouldBeEmpty(
            "A whole-value pattern ends in '$', which also matches before a trailing line feed. End it with '\\z':\n"
            + string.Join('\n', violations));
    }

    [Fact]
    public void The_scan_flags_a_dollar_ending_in_every_form_a_pattern_is_written_and_accepts_the_absolute_end()
    {
        var flagged = Scan("""
            [GeneratedRegex("^[a-z]+$", RegexOptions.CultureInvariant)]
            private static partial Regex A();

            [GeneratedRegex(@"^[a-z]+\z", RegexOptions.CultureInvariant)]
            private static partial Regex B();

            [GeneratedRegex("^[a-z]+\\z", RegexOptions.CultureInvariant)]
            private static partial Regex C();

            [GeneratedRegex(
                @"^[a-z]+$",
                RegexOptions.CultureInvariant)]
            private static partial Regex D();

            [GeneratedRegex(@"\bcontent\b|[a-z]+$", RegexOptions.CultureInvariant)]
            private static partial Regex E();

            private static readonly Regex F = new(@"^[a-z]+$", RegexOptions.None);
            var g = new Regex("^[a-z]+\\z");
            var h = Regex.IsMatch(value, "^[a-z]+$");
            var i = Regex.IsMatch(value, pattern);
            var j = new Regex(@"^[a-z]+$", RegexOptions.None, TimeSpan.FromSeconds(1));
            """).ToList();

        flagged.Count.ShouldBe(9, "the variable pattern of line 21 is not a literal and is not a site");
        flagged.Where(static found => found.StartsAnchored && !found.EndsAbsolute).Select(static found => found.Line)
            .ShouldBe([1, 10, 18, 20, 22]);
        flagged.Single(static found => !found.StartsAnchored).Pattern.ShouldBe(@"\bcontent\b|[a-z]+$");
    }

    private static IEnumerable<(string Pattern, int Line, bool StartsAnchored, bool EndsAbsolute)> Scan(string source)
    {
        foreach (Match match in Site.Matches(source))
        {
            var pattern = match.Groups["verbatim"].Success
                ? match.Groups["verbatim"].Value.Replace("\"\"", "\"", StringComparison.Ordinal)
                : match.Groups["regular"].Value.Replace("\\\\", "\\", StringComparison.Ordinal)
                    .Replace("\\\"", "\"", StringComparison.Ordinal);

            var line = source.AsSpan(0, match.Index).Count('\n') + 1;

            yield return (
                pattern,
                line,
                pattern.StartsWith('^'),
                pattern.EndsWith(@"\z", StringComparison.Ordinal));
        }
    }

    private static IEnumerable<string> SourceFiles()
        => Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(static file =>
            {
                var separator = Path.DirectorySeparatorChar;

                return !file.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                    && !file.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
                    && !file.Contains($"{separator}node_modules{separator}", StringComparison.Ordinal);
            });
}
