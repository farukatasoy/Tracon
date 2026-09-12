using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Tracon.Generators;

namespace Tracon.Generators.UnitTests;

/// <summary>Test helper that runs the generator against a synthetic compilation.</summary>
internal static class GeneratorTestHelper
{
    private static readonly ImmutableArray<MetadataReference> References = BuildReferences();

    /// <summary>Runs the given source through <see cref="ToolRegistrationGenerator"/>.</summary>
    public static GeneratorRunResult Run(string source)
        => RunWithDriver(CreateDriver(), source);

    /// <summary>
    /// Runs the generator against <paramref name="firstSource"/>, then against
    /// <paramref name="secondSource"/> with the SAME DRIVER (preserving the cache).
    /// Incrementality (52.5, the <c>IIncrementalGenerator</c> contract) is verified by
    /// inspecting the step cache reasons of this second run.
    /// </summary>
    public static (GeneratorRunResult First, GeneratorRunResult Second) RunIncremental(string firstSource, string secondSource)
    {
        var driver = CreateDriver();
        var first = RunWithDriverCore(driver, firstSource, out var afterFirst);
        var second = RunWithDriverCore(afterFirst, secondSource, out _);

        return (first, second);
    }

    private static CSharpGeneratorDriver CreateDriver()
        => CSharpGeneratorDriver.Create(
            [new ToolRegistrationGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

    private static GeneratorRunResult RunWithDriver(CSharpGeneratorDriver driver, string source)
        => RunWithDriverCore(driver, source, out _);

    private static GeneratorRunResult RunWithDriverCore(CSharpGeneratorDriver driver, string source, out CSharpGeneratorDriver nextDriver)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));

        var compilation = CSharpCompilation.Create(
            "Tracon.Generators.Tests.Subject",
            [syntaxTree],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        nextDriver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        return new GeneratorRunResult(compilation, (CSharpCompilation)outputCompilation, diagnostics, nextDriver.GetRunResult());
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();

        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);

        foreach (var path in trustedAssemblies)
        {
            builder.Add(MetadataReference.CreateFromFile(path));
        }

        builder.Add(MetadataReference.CreateFromFile(typeof(TraconToolAttribute).Assembly.Location));
        builder.Add(MetadataReference.CreateFromFile(typeof(ITraconBuilder).Assembly.Location));

        return builder.ToImmutable();
    }
}

/// <summary>The result of one generator run.</summary>
internal sealed record GeneratorRunResult(
    CSharpCompilation InputCompilation,
    CSharpCompilation OutputCompilation,
    ImmutableArray<Diagnostic> Diagnostics,
    GeneratorDriverRunResult RunResult)
{
    /// <summary>Returns the text of every generated source file (file name -&gt; content).</summary>
    public IReadOnlyDictionary<string, string> GeneratedFiles()
        => RunResult.Results
            .SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);

    /// <summary>Returns only the diagnostics with a specific id (example: TRC0001).</summary>
    public IReadOnlyList<Diagnostic> DiagnosticsWithId(string id)
        => [.. Diagnostics.Where(d => string.Equals(d.Id, id, StringComparison.Ordinal))];

    /// <summary>Returns the text of the SINGLE wrapper file outside the aggregator (for single-tool tests).</summary>
    public string SingleWrapperFile()
        => GeneratedFiles().Single(kv => !string.Equals(kv.Key, "TraconGeneratedTools.g.cs", StringComparison.Ordinal)).Value;

    /// <summary>
    /// When more than one tool is generated, returns the text of the SINGLE wrapper file
    /// whose hint name starts with <paramref name="hintNamePrefix"/>.
    /// </summary>
    public string SingleWrapperFile(string hintNamePrefix)
        => GeneratedFiles().Single(kv => kv.Key.StartsWith(hintNamePrefix, StringComparison.Ordinal)).Value;

    /// <summary>
    /// Returns the cache reasons (<see cref="IncrementalStepRunReason"/>) for this run,
    /// for the step marked with <paramref name="trackingName"/>.
    /// </summary>
    public IReadOnlyList<IncrementalStepRunReason> StepReasons(string trackingName)
        => [.. RunResult.Results
            .SelectMany(r => r.TrackedSteps.TryGetValue(trackingName, out var steps) ? steps : [])
            .SelectMany(step => step.Outputs.Select(output => output.Reason))];
}
