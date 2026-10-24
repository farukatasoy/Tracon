using System.Text.RegularExpressions;

namespace Tracon.Client.UnitTests;

/// <summary>
/// The generated client declares no type whose name ends in a digit (Phase 238, section 238.2).
/// </summary>
/// <remarks>
/// <para>
/// NSwag settles a name clash between two schemas by numbering the second one: <c>SourceKind</c> and
/// <c>SourceKind2</c>. The number is an accident of generation order. Add a schema that sorts earlier and the two
/// swap, and a consumer that wrote <c>SourceKind2</c> now holds another type; the OpenAPI snapshot and the
/// client coverage gates do not see it because both documents are still consistent with each other.
/// </para>
/// <para>
/// The count is zero today, so the test holds it there. A clash is solved at the source, by giving one of the two
/// schemas a distinct name, never by accepting a numbered one.
/// </para>
/// </remarks>
public sealed class GeneratedNameCollisionTests
{
    private static readonly Regex TypeDeclaration = new(
        @"\b(?:class|record|enum|struct|interface)\s+(?<name>[A-Za-z_]\w*)\b",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void No_generated_type_name_ends_in_a_digit()
    {
        var numbered = new SortedSet<string>(StringComparer.Ordinal);
        var declared = 0;

        foreach (var path in Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "src", "Tracon.Client", "Generated"), "*.cs"))
        {
            foreach (Match match in TypeDeclaration.Matches(File.ReadAllText(path)))
            {
                declared++;
                var name = match.Groups["name"].Value;

                if (char.IsAsciiDigit(name[^1]))
                {
                    numbered.Add(name);
                }
            }
        }

        // A scan that read nothing would pass for the wrong reason. 2026-10-24: 334 declarations.
        declared.ShouldBeGreaterThan(300);

        numbered.ShouldBeEmpty(
            "NSwag numbered a type to settle a name clash. Rename one of the two schemas in the server so the " +
            "names differ, then regenerate the client.");
    }

    [Theory]
    [InlineData("public enum SourceKind2 { A }", "SourceKind2")]
    [InlineData("public partial class Run3 { }", "Run3")]
    public void The_scan_reads_a_numbered_declaration(string line, string name)
    {
        TypeDeclaration.Match(line).Groups["name"].Value.ShouldBe(name);
        char.IsAsciiDigit(name[^1]).ShouldBeTrue();
    }
}
