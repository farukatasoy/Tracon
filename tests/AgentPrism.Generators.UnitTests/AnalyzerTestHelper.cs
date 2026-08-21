using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
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
    /// <summary>
    /// Every trusted-platform assembly, as a metadata reference. Shared with
    /// <c>Examples.ExampleCompilationTests</c>, which compiles a synthetic
    /// consumer too and would otherwise duplicate <see cref="BuildReferences"/>.
    /// </summary>
    internal static readonly ImmutableArray<MetadataReference> References = BuildReferences();

    /// <summary>
    /// The build properties the package makes compiler-visible. APG0402 only
    /// speaks while the local reference file is written, so the tests that
    /// expect it have to say so, exactly as the build target does.
    /// </summary>
    public static readonly (string Key, string Value)[] LocalReferenceOn =
        [("build_property.AgentPrismWriteLocalReference", "true")];

    /// <summary>Runs the analyzer and returns the diagnostics it reported.</summary>
    /// <param name="source">Consumer source code.</param>
    /// <param name="additionalFiles">
    /// Files the package's build target supplies; APG0401 and APG0402 read
    /// <c>AGENTS.md</c> and <c>AgentPrism.AgentMap.md</c> from here.
    /// </param>
    public static Task<ImmutableArray<Diagnostic>> RunAsync(
        string source,
        params (string Path, string Text)[] additionalFiles)
        => RunAsync([source], additionalFiles);

    /// <summary>
    /// Runs the analyzer with the build properties a real consumer build would
    /// make visible, on top of the additional files.
    /// </summary>
    public static Task<ImmutableArray<Diagnostic>> RunWithPropertiesAsync(
        string source,
        (string Key, string Value)[] properties,
        params (string Path, string Text)[] additionalFiles)
        => RunAsync([source], additionalFiles, properties);

    /// <summary>
    /// Runs the analyzer over several source files of ONE compilation. APG0101
    /// and APG0102 look at the whole compilation, so "the registration is in
    /// another file" must be shown to stay silent.
    /// </summary>
    public static async Task<ImmutableArray<Diagnostic>> RunAsync(
        IReadOnlyList<string> sources,
        (string Path, string Text)[]? additionalFiles = null,
        (string Key, string Value)[]? properties = null)
    {
        var compilation = CSharpCompilation.Create(
            "ConsumerApplication",
            sources.Select(source => CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))),
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var options = new AnalyzerOptions(
            [.. (additionalFiles ?? []).Select(file => (AdditionalText)new InMemoryAdditionalText(file.Path, file.Text))],
            new InMemoryOptionsProvider(properties ?? []));

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

    /// <summary>
    /// Supplies the <c>build_property.*</c> entries that the SDK writes into a
    /// generated .editorconfig from <c>CompilerVisibleProperty</c> items.
    /// </summary>
    private sealed class InMemoryOptionsProvider((string Key, string Value)[] properties) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new InMemoryOptions(properties);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class InMemoryOptions((string Key, string Value)[] properties) : AnalyzerConfigOptions
        {
            private readonly Dictionary<string, string> _values =
                properties.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
                => _values.TryGetValue(key, out value);
        }
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default)
            => SourceText.From(text, Encoding.UTF8);
    }
}
