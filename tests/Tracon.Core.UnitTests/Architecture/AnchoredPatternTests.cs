using System.Text.RegularExpressions;
using Tracon.Tests.Common;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every source-generated pattern that validates a whole value ends in <c>\z</c>, never in <c>$</c> (phase 260, F-756).
/// </summary>
/// <remarks>
/// <para>
/// In .NET <c>$</c> matches at the very end of the text AND before a trailing <c>\n</c>, so
/// <c>^[a-z0-9]+$</c> accepts <c>"acme\n"</c>. Six validators wrote it that way: a tenant id, a custom event type,
/// a job handler key, a job lane, a knowledge collection and an MCP name. A claim an identity provider issued, or a
/// route segment, can carry the line feed (a header cannot), and the value then became a name no request could
/// ever write again.
/// </para>
/// <para>
/// The gate reads the source, because the attribute argument is the only place the pattern is written once. A
/// pattern that starts with <c>^</c> is a whole-value validator and must end with <c>\z</c>. Patterns that look
/// INSIDE a text (a content matcher) do not start with <c>^</c> and are out of scope.
/// </para>
/// </remarks>
public sealed class AnchoredPatternTests
{
    // A pattern literal as written in source: a verbatim @"..." (a doubled quote is a quote) or a regular "..."
    // (a backslash escapes the next character). The raw string form is not used by any pattern.
    private static readonly Regex Attribute = new(
        """\[GeneratedRegex\(\s*(?:@"(?<verbatim>(?:[^"]|"")*)"|"(?<regular>(?:[^"\\]|\\.)*)")""",
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

        // A scan that found nothing would pass for the wrong reason. 2026-10-29: seven whole-value patterns
        // (the six of F-756 and the audit chain key id).
        anchored.ShouldBeGreaterThanOrEqualTo(7);

        violations.ShouldBeEmpty(
            "A whole-value pattern ends in '$', which also matches before a trailing line feed. End it with '\\z':\n"
            + string.Join('\n', violations));
    }

    [Fact]
    public void The_scan_flags_a_dollar_ending_and_accepts_the_absolute_end()
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
            """).ToList();

        flagged.Count.ShouldBe(5);
        flagged.Where(static found => found.StartsAnchored && !found.EndsAbsolute).Select(static found => found.Line)
            .ShouldBe([1, 10]);
        flagged.Single(static found => !found.StartsAnchored).Pattern.ShouldBe(@"\bcontent\b|[a-z]+$");
    }

    private static IEnumerable<(string Pattern, int Line, bool StartsAnchored, bool EndsAbsolute)> Scan(string source)
    {
        foreach (Match match in Attribute.Matches(source))
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
