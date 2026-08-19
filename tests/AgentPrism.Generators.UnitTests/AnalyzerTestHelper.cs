using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace AgentPrism.Generators.UnitTests;

/// <summary>
/// Test helper that runs <see cref="AgentPrismUsageAnalyzer"/> against a
/// synthetic consumer compilation.
/// </summary>
/// <remarks>
/// The compilation is deliberately NOT named <c>AgentPrism.*</c>: the analyzer
/// only reports on symbols that come from an AgentPrism assembly, and a test
/// assembly with that name would satisfy the check by accident. The AgentPrism
/// assemblies are referenced for real instead.
/// </remarks>
internal static class AnalyzerTestHelper
{
    private static readonly ImmutableArray<MetadataReference> References = BuildReferences();

    /// <summary>Runs the analyzer and returns the diagnostics it reported.</summary>
    /// <param name="source">Consumer source code.</param>
    /// <param name="additionalFiles">
    /// Files the package's build target supplies; APG0401 reads
    /// <c>AGENTS.md</c> and <c>AgentPrism.AgentMap.md</c> from here.
    /// </param>
    public static Task<ImmutableArray<Diagnostic>> RunAsync(
        string source,
        params (string Path, string Text)[] additionalFiles)
        => RunAsync([source], additionalFiles);

    /// <summary>
    /// Runs the analyzer over several source files of ONE compilation. APG0101
    /// and APG0102 look at the whole compilation, so "the registration is in
    /// another file" must be shown to stay silent.
    /// </summary>
    public static async Task<ImmutableArray<Diagnostic>> RunAsync(
        IReadOnlyList<string> sources,
        params (string Path, string Text)[] additionalFiles)
    {
        var compilation = CSharpCompilation.Create(
            "ConsumerApplication",
            sources.Select(source => CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))),
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var options = new AnalyzerOptions(
            [.. additionalFiles.Select(file => (AdditionalText)new InMemoryAdditionalText(file.Path, file.Text))]);

        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new AgentPrismUsageAnalyzer()),
            options);

        return await withAnalyzers.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Asserts that the consumer source itself compiles without errors.</summary>
    public static void ShouldCompileCleanly(string source)
    {
        var compilation = CSharpCompilation.Create(
            "ConsumerApplication",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToList();

        errors.ShouldBeEmpty();
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);

        foreach (var path in trustedAssemblies)
        {
            builder.Add(MetadataReference.CreateFromFile(path));
        }

        return builder.ToImmutable();
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default)
            => SourceText.From(text, Encoding.UTF8);
    }
}
