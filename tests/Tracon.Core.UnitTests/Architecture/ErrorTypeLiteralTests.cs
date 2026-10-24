using System.Reflection;
using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// No error code is written as a string literal outside <c>TraconErrorTypes</c> and <c>ValidationCodes</c> (Phase 238, section 238.3).
/// </summary>
/// <remarks>
/// <para>
/// Two scans over <c>src/</c>. The first finds a place that WRITES a code (a <c>*ErrorType</c> assignment, a
/// <c>RunError.Type</c> initializer, an error <c>type:</c> argument, a job code constant) with a literal: that is
/// a code the catalog and its snapshot do not know. The second finds any literal that equals a catalog value, which
/// catches a second spelling of a known code (a positional argument the first scan cannot read).
/// </para>
/// <para>
/// The noise is excluded by PATH and by what the line is, never by the value: a value is exactly what the scan
/// must not trust. The excluded places are named below with the reason.
/// </para>
/// </remarks>
public sealed class ErrorTypeLiteralTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private static readonly string[] CatalogFiles =
    [
        "src/Tracon.Abstractions/TraconErrorTypes.cs",
        "src/Tracon.Core/Compilation/ValidationCodes.cs",
    ];

    // A place that writes a code. Case-sensitive on purpose: `Type: "message"` (a positional record argument of an
    // OpenAI wire object) and `type = "enabled"` (a provider's own vocabulary) are not Tracon error codes. A job
    // code has a hyphen in its last segment (`tracon.job.session-save-failed`); the metric and attribute names
    // under the same prefix (`tracon.job.executions`) do not.
    // `type:` counts as a named argument only after `(`, `,` or a line start: prose such as "a settings type: " in a
    // message string is not one.
    private static readonly Regex[] WritePatterns =
    [
        new(@"\b\w*ErrorType\w*\s*=>?\s*""", RegexOptions.CultureInvariant, Timeout),
        new(@"\bType\s*=\s*""", RegexOptions.CultureInvariant, Timeout),
        new(@"(?:^|[(,])\s*type:\s*""", RegexOptions.CultureInvariant, Timeout),
        new(@"\bstring\s+type\s*=\s*""", RegexOptions.CultureInvariant, Timeout),
        new(@"\bconst\s+string\s+\w+\s*=\s*""tracon\.job\.[a-z0-9]+-", RegexOptions.CultureInvariant, Timeout),

        // The code of a validation finding: `Code = "unknown_tool"`, or a constant named `...Code` holding a snake_case code.
        new(@"\bCode\s*=\s*""", RegexOptions.CultureInvariant, Timeout),
        new(@"\bconst\s+string\s+\w+Code\s*=\s*""[a-z]+(?:_[a-z]+)*"";", RegexOptions.CultureInvariant, Timeout),
    ];

    // Places that hold no Tracon error code, whatever their lines look like.
    private static readonly (string PathPart, string Reason)[] ExcludedPaths =
    [
        ("src/Tracon.Testing.Contracts.Xunit/", "Fixtures of the store contracts: arbitrary strings, and the pins that prove a persisted value never changes."),
        ("src/Tracon.OpenAI/Live/OpenAILiveProvider.cs", "The OpenAI live transport kind (\"webrtc\"), not an error."),
    ];

    // Literals that equal a catalog value but are not the code: another vocabulary that happens to use the word.
    private static readonly (string PathPart, string Value, string Reason)[] AllowedValueSites =
    [
        ("src/Tracon.Sql.Shared/Stores/SqlRunStore.cs", "orphaned", "The error fingerprint of an orphaned run, not its error type."),
        ("src/Tracon.Core/Storage/InMemoryRunStore.Lifecycle.cs", "orphaned", "The error fingerprint of an orphaned run, not its error type."),
        ("src/Tracon.Core/Models/FallbackChatClient.cs", "provider_unavailable", "The skip-reason label of a fallback hop, not an error type."),
    ];

    [Fact]
    public void No_source_file_writes_an_error_code_as_a_literal()
    {
        var found = Scan(SourceFiles(), CatalogValues());

        found.ShouldBeEmpty(
            "An error code is written as a string literal. Add it to TraconErrorTypes (src/Tracon.Abstractions/" +
            "TraconErrorTypes.cs; for a validation finding, ValidationCodes in src/Tracon.Core/Compilation/), " +
            "refresh its snapshot (ContractSnapshot names the refresh variable), and use the constant.");
    }

    [Fact]
    public void The_scan_reads_the_source_tree()
    {
        // A scan over an empty tree passes the test above for the wrong reason.
        SourceFiles().Count(static file => file.Path.StartsWith("src/Tracon.AspNetCore/OpenAICompat/", StringComparison.Ordinal)).ShouldBeGreaterThan(3);
        SourceFiles().Count().ShouldBeGreaterThan(1000);
    }

    [Theory]
    [InlineData("src/Tracon.AspNetCore/Endpoints/X.cs", "return OpenAICompatSupport.Error(400, m, type: \"x_y\");")]
    [InlineData("src/Tracon.Core/X.cs", "public const string FooErrorType = \"foo_failed\";")]
    [InlineData("src/Tracon.Core/X.cs", "var error = new RunError { Type = \"foo_failed\", Message = m };")]
    [InlineData("src/Tracon.Core/X.cs", "public override string ErrorType => \"foo_failed\";")]
    [InlineData("src/Tracon.Core/X.cs", "internal const string RetryCode = \"tracon.job.retry-later\";")]
    [InlineData("src/Tracon.AspNetCore/X.cs", "static IResult Error(int status, string type = \"invalid_request_error\")")]
    [InlineData("src/Tracon.AspNetCore/X.cs", "await WriteErrorAsync(writer, text, \"upstream_error\", token);")]
    [InlineData("src/Tracon.Core/Compilation/X.cs", "messages.Add(new ValidationMessage { Code = \"new_finding\", Message = m });")]
    [InlineData("src/Tracon.Core/Compilation/X.cs", "internal const string NewFindingCode = \"new_finding\";")]
    [InlineData("src/Tracon.Core/Tools/X.cs", "problems.Add(new HostedToolProblem(\n    \"unknown_tool\",\n    m, p));")]
    public void The_scan_flags_each_way_of_writing_a_literal_code(string path, string line)
    {
        Scan([new SourceFile(path, line)], CatalogValues()).Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("src/Tracon.Core/X.cs", "var error = new RunError { Type = TraconErrorTypes.Orphaned };")]
    [InlineData("src/Tracon.Core/Compilation/X.cs", "messages.Add(new ValidationMessage { Code = ValidationCodes.UnknownTool });")]
    [InlineData("src/Tracon.Core/Compilation/ValidationCodes.cs", "public const string UnknownTool = \"unknown_tool\";")]
    [InlineData("src/Tracon.Core/X.cs", "// ErrorType = \"foo_failed\" in a comment")]
    [InlineData("src/Tracon.Core/X.cs", "/// <c>type: \"foo_failed\"</c> in a doc comment")]
    [InlineData("src/Tracon.AspNetCore/OpenAICompat/X.cs", "new ConversationItem(Id: id, Type: \"message\")")]
    [InlineData("src/Tracon.Abstractions/TraconErrorTypes.cs", "public const string Orphaned = \"orphaned\";")]
    [InlineData("src/Tracon.OpenAI/Live/OpenAILiveProvider.cs", "Transport = new OpenAILiveTransport { Type = \"webrtc\" },")]
    [InlineData("src/Tracon.Testing.Contracts.Xunit/Contracts/X.cs", "Error = new RunError { Type = \"test_error\" },")]
    [InlineData("src/Tracon.Core/Storage/InMemoryRunStore.Lifecycle.cs", "private const string OrphanedFingerprint = \"orphaned\";")]
    public void The_scan_leaves_alone_what_is_not_an_error_code(string path, string line)
    {
        Scan([new SourceFile(path, line)], CatalogValues()).ShouldBeEmpty();
    }

    private static List<string> Scan(IEnumerable<SourceFile> files, IReadOnlyCollection<string> catalog)
    {
        var found = new List<string>();

        foreach (var file in files)
        {
            if (CatalogFiles.Contains(file.Path, StringComparer.Ordinal))
            {
                continue;
            }

            var excludedPath = ExcludedPaths.Any(excluded => file.Path.Contains(excluded.PathPart, StringComparison.Ordinal));
            var lineNumber = 0;

            foreach (var line in file.Text.Split('\n'))
            {
                lineNumber++;

                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!excludedPath && WritePatterns.Any(pattern => pattern.IsMatch(line)))
                {
                    found.Add($"{file.Path}:{lineNumber}: a code is written as a literal: {line.Trim()}");
                    continue;
                }

                if (excludedPath)
                {
                    continue;
                }

                foreach (var value in catalog.Where(value => line.Contains($"\"{value}\"", StringComparison.Ordinal)))
                {
                    if (!AllowedValueSites.Any(site => file.Path.Contains(site.PathPart, StringComparison.Ordinal) &&
                                                      string.Equals(site.Value, value, StringComparison.Ordinal)))
                    {
                        found.Add($"{file.Path}:{lineNumber}: the literal \"{value}\" is a catalog value: {line.Trim()}");
                    }
                }
            }
        }

        return found;
    }

    private static IEnumerable<SourceFile> SourceFiles()
    {
        var root = RepoRoot.Path;

        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');

            if (relative.Contains("/obj/", StringComparison.Ordinal) ||
                relative.Contains("/bin/", StringComparison.Ordinal) ||
                relative.Contains("/node_modules/", StringComparison.Ordinal) ||
                relative.Contains("/Generated/", StringComparison.Ordinal))
            {
                continue;
            }

            yield return new SourceFile(relative, File.ReadAllText(path));
        }
    }

    private static List<string> CatalogValues() => new[] { typeof(TraconErrorTypes), typeof(ValidationCodes) }
        .SelectMany(static catalog => catalog.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        .Where(static field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(static field => (string)field.GetRawConstantValue()!)
        .ToList();

    private sealed record SourceFile(string Path, string Text);
}
