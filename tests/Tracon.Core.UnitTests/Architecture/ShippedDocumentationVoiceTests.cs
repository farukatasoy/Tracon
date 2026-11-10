using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Keeps the text that ships to a consumer in the voice of documentation: no alarm emoji, no
/// <c>Rationale:</c> or <c>Decision:</c> openers and no dated <c>Measured (…)</c> blocks.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SourceLanguageTests"/> settled the language question — a shipped document is English.
/// This one settles the voice question. Alarm emoji, a <c>Rationale:</c> opener and a dated
/// measurement are notes from one maintainer to the next, not API documentation, and a consumer reads
/// the package text as it ships. The documentation-site generators already strip them before
/// publishing, which is the project's own judgement that they do not belong on a consumer surface.
/// The package ships the unstripped text, so the same judgement is enforced at the source.
/// </para>
/// <para>
/// The rule covers what reaches a consumer: <c>///</c> blocks in <c>src/</c> (the build packs them as
/// XML documentation), package READMEs (nuget.org renders them), the project template's content (it
/// becomes the consumer's own project), <c>buildTransitive/</c> files (imported into every consumer
/// build), the licence files and the packed OpenAPI document. Every other file is outside it.
/// </para>
/// <para>
/// There is no baseline: the rule starts at zero and a new offending line fails the build.
/// </para>
/// </remarks>
public sealed class ShippedDocumentationVoiceTests
{
    /// <remarks>Declared first: a static initialiser below reads it.</remarks>
    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>
    /// The development journal's voice. 'Rationale:' and 'Decision:' are the journal's labels and are
    /// capitalised; lower-case 'rationale' and 'decision' are ordinary English and stay. Only the dated
    /// measurement is matched either way.
    /// </summary>
    private static readonly Regex InternalVoicePattern = new(
        @"🚨|⚠️|\bRationale:|\bDecision:\s|(?i:\bmeasured \(20\d\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void Text_that_ships_to_a_consumer_keeps_the_voice_of_documentation()
    {
        var offending = new List<string>();

        foreach (var relative in TrackedFiles())
        {
            var rules = RulesFor(relative);

            if (rules == Rules.None)
            {
                continue;
            }

            var absolute = Path.Combine(RepositoryRoot, relative);

            if (!File.Exists(absolute) || IsBinary(absolute))
            {
                continue;
            }

            offending.AddRange(
                OffendingLines(File.ReadAllLines(absolute), rules).Select(line => $"{relative}:{line + 1}"));
        }

        offending.Sort(StringComparer.Ordinal);

        offending.ShouldBeEmpty(
            customMessage: "These lines ship to a consumer in the journal's voice (alarm emoji, a 'Rationale:' " +
                           $"or 'Decision:' opener, or a dated 'Measured (…)' block). Say it as documentation:{Environment.NewLine}" +
                           string.Join(Environment.NewLine, offending));
    }

    [Theory]
    [InlineData("src/Tracon.Core/Foo.cs", "VoiceDocumentationComments")]
    [InlineData("src/Tracon.Core/README.md", "VoiceEveryLine")]
    [InlineData("src/Tracon.Templates/content/Program.cs", "VoiceEveryLine")]
    [InlineData("src/Tracon.Core/buildTransitive/Tracon.Core.targets", "VoiceEveryLine")]
    [InlineData("README.md", "VoiceEveryLine")]
    [InlineData("LICENSE.md", "VoiceEveryLine")]
    [InlineData("LICENSE-MIT.md", "VoiceEveryLine")]
    [InlineData("docs/openapi/tracon.json", "VoiceEveryLine")]
    [InlineData("src/Tracon.Templates/content/.template.config/template.json", "None")]
    [InlineData("src/Tracon.Core/Nested/README.md", "None")]
    [InlineData("tests/Tracon.Core.UnitTests/FooTests.cs", "None")]
    [InlineData("scripts/gate.py", "None")]
    [InlineData("CHANGELOG.md", "None")]
    public void The_voice_rule_covers_what_reaches_a_consumer(string path, string expected)
    {
        RulesFor(path).ToString().ShouldBe(expected);
    }

    [Fact]
    public void The_voice_rule_applies_only_where_the_text_ships()
    {
        string[] lines = ["🚨 a note to the next maintainer", "plain text"];

        OffendingLines(lines, Rules.None).ShouldBeEmpty();
        OffendingLines(lines, Rules.VoiceEveryLine).Count.ShouldBe(1);
    }

    [Fact]
    public void The_voice_rule_on_a_source_file_reads_documentation_comments_only()
    {
        string[] lines =
        [
            "// 🚨 an implementation comment stays",
            "/// <summary>Measured (2026-09-04): fast.</summary>",
        ];

        OffendingLines(lines, Rules.VoiceDocumentationComments).Count.ShouldBe(1);
    }

    [Fact]
    public void A_voice_phrase_that_wraps_across_documentation_lines_is_attributed_to_both()
    {
        string[] lines =
        [
            "/// <summary>Measured",
            "/// (2026-09-04): fast.</summary>",
            "/// <remarks>plain</remarks>",
        ];

        OffendingLines(lines, Rules.VoiceDocumentationComments).ShouldBe([0, 1]);
    }

    [Theory]
    [InlineData("Every operation takes a rationale and a decision.")]
    [InlineData("The decision: reject. The rationale is below.")]
    [InlineData("Cancellation is observed at the next await.")]
    public void Ordinary_english_is_not_the_journals_voice(string line)
    {
        OffendingLines([line], Rules.VoiceEveryLine).ShouldBeEmpty();
    }

    /// <summary>
    /// What reaches a consumer: the project template's content becomes the consumer's own project and
    /// <c>buildTransitive/</c> is imported into every consumer build; a package README is
    /// <c>PackageReadmeFile</c> and nuget.org renders it; <c>PackageLicenseFile</c> packs the licence
    /// files; the OpenAPI document is packaged into <c>Tracon.AspNetCore</c>; and
    /// <c>GenerateDocumentationFile</c> ships every <c>///</c> block as XML.
    /// </summary>
    private static Rules RulesFor(string relative)
    {
        var shippedWhole =
            relative.StartsWith("src/", StringComparison.Ordinal) &&
            (relative.Contains("/content/", StringComparison.Ordinal) ||
             relative.Contains("/buildTransitive/", StringComparison.Ordinal)) &&
            !relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        var packageReadme =
            relative.StartsWith("src/", StringComparison.Ordinal) &&
            relative.EndsWith("/README.md", StringComparison.Ordinal) &&
            relative.Count(character => character == '/') == 2;

        var shippedFile = relative is "README.md" or "LICENSE.md" or "LICENSE-MIT.md" or "docs/openapi/tracon.json";

        if (shippedWhole || packageReadme || shippedFile)
        {
            return Rules.VoiceEveryLine;
        }

        if (relative.StartsWith("src/", StringComparison.Ordinal) &&
            relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return Rules.VoiceDocumentationComments;
        }

        return Rules.None;
    }

    /// <summary>
    /// Tracked files plus untracked ones that are not ignored, so a file added in the working tree is read
    /// before it is committed. Git decides what exists: a walk of the disk would read the generated and
    /// ignored trees the site build writes.
    /// </summary>
    private static List<string> TrackedFiles()
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        foreach (var argument in new[] { "ls-files", "--cached", "--others", "--exclude-standard", "-z" })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start 'git'. The voice scan lists files with git.");

        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'git ls-files' failed with exit code {process.ExitCode}: {errors.GetAwaiter().GetResult()}");
        }

        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToList();
    }

    private static bool IsBinary(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[8000];
        var read = stream.Read(buffer, 0, buffer.Length);

        return Array.IndexOf(buffer, (byte)0, 0, read) >= 0;
    }

    /// <summary>The zero-based indexes of the lines that break the rules, each counted once.</summary>
    private static List<int> OffendingLines(string[] lines, Rules rules)
    {
        var offending = new SortedSet<int>();

        if (rules.HasFlag(Rules.VoiceEveryLine))
        {
            for (var index = 0; index < lines.Length; index++)
            {
                if (InternalVoicePattern.IsMatch(lines[index]))
                {
                    offending.Add(index);
                }
            }
        }

        if (rules.HasFlag(Rules.VoiceDocumentationComments))
        {
            AddOffendingDocumentationLines(lines, offending);
        }

        return [.. offending];
    }

    /// <summary>
    /// A documentation comment is inspected as a block rather than line by line: the phrase "Measured
    /// (2026" wraps at the source width. A match is attributed to the lines it spans.
    /// </summary>
    private static void AddOffendingDocumentationLines(string[] lines, SortedSet<int> offending)
    {
        var block = new List<(int Line, string Text)>();

        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].AsSpan().TrimStart();

            if (trimmed.StartsWith("///"))
            {
                block.Add((index, trimmed[3..].ToString().Trim()));
                continue;
            }

            AddOffendingBlockLines(block, offending);
            block.Clear();
        }

        AddOffendingBlockLines(block, offending);
    }

    private static void AddOffendingBlockLines(List<(int Line, string Text)> block, SortedSet<int> offending)
    {
        if (block.Count == 0)
        {
            return;
        }

        var joined = string.Join(' ', block.Select(entry => entry.Text));

        foreach (var match in InternalVoicePattern.Matches(joined).Cast<Match>())
        {
            var cursor = 0;

            foreach (var (line, text) in block)
            {
                var end = cursor + text.Length;

                if (match.Index < end && match.Index + match.Length > cursor)
                {
                    offending.Add(line);
                }

                cursor = end + 1;
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}' for Tracon.slnx.");
    }

    /// <summary>Which rules apply to a file.</summary>
    [Flags]
    private enum Rules
    {
        /// <summary>The file ships to nobody; no rule applies.</summary>
        None = 0,

        /// <summary>Every line is checked for the journal's voice.</summary>
        VoiceEveryLine = 1,

        /// <summary>Only <c>///</c> blocks are checked for the journal's voice.</summary>
        VoiceDocumentationComments = 2,
    }
}
