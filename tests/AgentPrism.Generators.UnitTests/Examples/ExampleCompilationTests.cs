using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AgentPrism.Generators.UnitTests.Examples;

/// <summary>
/// Compiles every shipped <c>&lt;example&gt;</c> block. <c>CapabilityExampleTests</c>
/// proves a block exists; this proves the coding agent that copies it gets code
/// that builds, not a name check that a broken block would still pass.
/// </summary>
/// <remarks>
/// Four of the 49 blocks carry <c>&lt;code language="json"&gt;</c>: an
/// <c>appsettings.json</c> fragment, not C#. Those are validated as JSON
/// instead of routed through Roslyn.
/// </remarks>
public sealed class ExampleCompilationTests
{
    private static string RepositoryRoot { get; } = ExampleExtractor.FindRepositoryRoot();

    private static readonly IReadOnlyList<ExampleBlock> Blocks = ExampleExtractor.ExtractAll(RepositoryRoot);

    public static TheoryData<string> Origins() => [.. Blocks.Select(block => block.Origin)];

    /// <summary>
    /// Guards the trap a gate like this can fall into silently: an extractor
    /// that skips a malformed block still reports a non-empty, plausible-looking
    /// count and stays green. The count here is cross-checked against a second,
    /// independent count of the same tag.
    /// </summary>
    [Fact]
    public void Every_example_tag_is_extracted_as_a_block()
    {
        var rawCount = ExampleExtractor.CountRawExampleTags(RepositoryRoot);

        Blocks.ShouldNotBeEmpty("No <example> block was found under src/; the search path or the parser regressed.");

        Blocks.Count.ShouldBe(
            rawCount,
            $"The parser produced {Blocks.Count} blocks; the source tree carries {rawCount} <example> tags. " +
            "One was silently dropped, most likely one without a <code> child.");
    }

    [Theory]
    [MemberData(nameof(Origins))]
    public void Every_example_block_compiles(string origin)
    {
        var block = Blocks.Single(candidate => string.Equals(candidate.Origin, origin, StringComparison.Ordinal));

        if (string.Equals(block.Language, "json", StringComparison.Ordinal))
        {
            AssertValidJson(block);

            return;
        }

        var source = ExamplePrelude.Wrap(block.Code);

        var compilation = CSharpCompilation.Create(
            "ExampleHarness",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            AnalyzerTestHelper.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToList();

        errors.ShouldBeEmpty($"{origin} does not compile:\n{string.Join('\n', errors)}\n\nSource shown:\n{block.Code}");
    }

    /// <summary>
    /// A configuration fragment is a bare property (<c>"ProviderSettings": { ... }</c>),
    /// not a full document, so it is wrapped in braces before parsing - the same
    /// shape it has once pasted into a real <c>appsettings.json</c> object.
    /// </summary>
    private static void AssertValidJson(ExampleBlock block)
    {
        JsonException? error = null;

        try
        {
            JsonDocument.Parse($"{{{block.Code}}}");
        }
        catch (JsonException exception)
        {
            error = exception;
        }

        error.ShouldBeNull($"{block.Origin} is not valid JSON: {error?.Message}\n\nSource shown:\n{block.Code}");
    }

    /// <summary>
    /// A package that ships an <c>&lt;example&gt;</c> but is missing from this test
    /// project's references would have its blocks skipped without anyone noticing
    /// - <see cref="Blocks"/> would simply never contain them. The found package
    /// set is compared against what this project actually references instead of
    /// listing the 15 packages by hand, so a 16th package added later is caught
    /// the same way.
    /// </summary>
    [Fact]
    public void Every_package_carrying_an_example_is_referenced_by_this_project()
    {
        var packagesWithExamples = Blocks.Select(block => block.Package).ToHashSet(StringComparer.Ordinal);

        var referencedPackages = Directory.GetFiles(AppContext.BaseDirectory, "AgentPrism.*.dll")
            .Select(file => Path.GetFileNameWithoutExtension(file)!)
            .ToHashSet(StringComparer.Ordinal);

        var missing = packagesWithExamples.Except(referencedPackages, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        missing.ShouldBeEmpty(
            $"These packages carry an <example> but are not referenced by AgentPrism.Generators.UnitTests, " +
            $"so their blocks are never compiled: {string.Join(", ", missing)}. Add a ProjectReference.");
    }
}
