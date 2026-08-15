using System.Text.RegularExpressions;

namespace AgentPrism.Core.UnitTests.Architecture;

/// <summary>
/// Enforces K-232: <c>ProblemDetails</c> <c>title</c> texts stay in English.
/// </summary>
/// <remarks>
/// <para>
/// HATA-S4-007 (MT-UI-032, family N): the server hardcoded Turkish ProblemDetails
/// titles — 113 distinct <c>title:</c> literals across 26 files. K-232 already
/// forbids that ("ProblemDetails texts ... stay in English"); this test makes the
/// rule permanent by scanning the sources.
/// </para>
/// <para>
/// Reads project sources from disk rather than compiled assemblies — the same
/// pattern as <see cref="DependencyDirectionTests"/>. The ASCII-Turkish word
/// pattern is applied only inside the body of a <c>title:</c> literal, which
/// keeps this test narrow and exact. The broad, baseline-driven rule for the
/// whole tree lives in <see cref="SourceLanguageTests"/>; the two do different
/// jobs and both are kept.
/// </para>
/// </remarks>
public sealed class ProblemDetailsLanguageTests
{
    private static readonly Regex TitleLiteralPattern = new(
        "title:\\s*\"(?<title>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    private static readonly Regex AsciiTurkishMarkerPattern = new(
        "(?i:\\b\\w*(?:bulunamadi|gecersiz|zorunlu|desteklenmiyor|eksik|kullanimda|basarisiz|kapali|olamaz|olmalidir|adinda|kimlikli|yetersiz|edilemedi|uyusmuyor|reddedildi|dogrulanamadi|catismasi|calistirilamadi|derlenemedi|engellendi)\\w*\\b)",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void Every_ProblemDetails_title_literal_is_English()
    {
        var root = RepositoryRoot;
        var srcRoot = Path.Combine(root, "src");
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var content = File.ReadAllText(file);

            foreach (Match match in TitleLiteralPattern.Matches(content))
            {
                var title = match.Groups["title"].Value;

                if (AsciiTurkishMarkerPattern.IsMatch(title))
                {
                    violations.Add($"{Path.GetRelativePath(root, file)}: title: \"{title}\"");
                }
            }
        }

        violations.ShouldBeEmpty(customMessage: string.Join('\n', violations));
    }

    /// <summary>
    /// Finds the repository root. The test output lives under artifacts/, so a
    /// fixed relative path cannot be used; walk upwards looking for AgentPrism.slnx.
    /// </summary>
    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgentPrism.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}' for AgentPrism.slnx.");
    }
}
