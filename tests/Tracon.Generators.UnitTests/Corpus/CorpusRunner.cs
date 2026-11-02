using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tracon.Generators.UnitTests.Corpus;

/// <summary>One diagnostic the package's analyzers reported on a corpus file.</summary>
/// <param name="Id">The diagnostic id.</param>
/// <param name="File">The file the diagnostic points at.</param>
/// <param name="Line">The 1-based line.</param>
/// <param name="Message">The message text.</param>
internal sealed record CorpusFinding(string Id, string File, int Line, string Message);

/// <summary>What running the package's analyzer and generator over one unit produced and cost.</summary>
/// <param name="Unit">The unit.</param>
/// <param name="Lines">Source lines read.</param>
/// <param name="Parse">Time spent parsing the files.</param>
/// <param name="Generator">Time spent in the source generator.</param>
/// <param name="Analyzer">Wall time spent in the analyzer (binding the unit's code included, since the analyzer asks for it).</param>
/// <param name="AnalyzerActions">Time the analyzer's own callbacks took, summed over threads.</param>
/// <param name="ActionsRun">How many callbacks the analyzer registered and the driver executed; zero means the run proved nothing.</param>
/// <param name="Findings">The usage diagnostics reported.</param>
/// <param name="Failures">Analyzer exceptions (AD0001), generator failures (CS8785) and exceptions the driver surfaced.</param>
/// <param name="SemanticErrors">Compiler errors of the unit, or -1 when not counted; high numbers mean most symbols did not bind and the run proved less.</param>
internal sealed record UnitReport(
    CorpusUnit Unit,
    int Lines,
    TimeSpan Parse,
    TimeSpan Generator,
    TimeSpan Analyzer,
    TimeSpan AnalyzerActions,
    int ActionsRun,
    ImmutableArray<CorpusFinding> Findings,
    ImmutableArray<string> Failures,
    int SemanticErrors);

/// <summary>
/// Runs <see cref="TraconUsageAnalyzer"/> and <see cref="ToolRegistrationGenerator"/> over a corpus of source text.
/// </summary>
/// <remarks>
/// <para>
/// The corpus is read, parsed and analysed; it is never built, restored or run. The references come from this test
/// assembly's own trusted platform assemblies (the runtime, ASP.NET Core and every Tracon package), so a corpus that
/// uses a library this assembly does not carry binds partially. That is deliberate: an analyzer that throws on code it
/// cannot fully bind is one of the two things the run exists to catch, the other being a runaway cost.
/// </para>
/// <para>
/// A unit is analysed as the package would meet it. The compilation is named after the unit for shipped code (a Tracon
/// assembly analysing itself reaches the analyzer's paths with the most symbols) and as a consumer assembly
/// otherwise; the unit's own assembly is taken out of the references so its types are defined once.
/// </para>
/// </remarks>
internal static class CorpusRunner
{
    private const string GlobalUsings = """
        global using global::System;
        global using global::System.Collections.Generic;
        global using global::System.IO;
        global using global::System.Linq;
        global using global::System.Net.Http;
        global using global::System.Threading;
        global using global::System.Threading.Tasks;
        global using global::Microsoft.AspNetCore.Builder;
        global using global::Microsoft.AspNetCore.Hosting;
        global using global::Microsoft.AspNetCore.Http;
        global using global::Microsoft.AspNetCore.Routing;
        global using global::Microsoft.Extensions.Configuration;
        global using global::Microsoft.Extensions.DependencyInjection;
        global using global::Microsoft.Extensions.Hosting;
        global using global::Microsoft.Extensions.Logging;
        """;

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    /// <summary>Analyses one unit.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="shipped">Whether the unit is Tracon's own shipped code, which is compiled under its own assembly name.</param>
    /// <param name="additionalFiles">Files the package's build target would hand the analyzer.</param>
    /// <param name="countSemanticErrors">Whether to bind the unit once more and count its compiler errors; costs a second pass.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The report.</returns>
    public static async Task<UnitReport> AnalyzeAsync(
        CorpusUnit unit,
        bool shipped,
        IReadOnlyList<(string Path, string Text)> additionalFiles,
        bool countSemanticErrors,
        CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var trees = new List<SyntaxTree>(unit.Files.Length + 1) { CSharpSyntaxTree.ParseText(GlobalUsings, ParseOptions, "GlobalUsings.cs", cancellationToken: cancellationToken) };
        var lines = 0;

        foreach (var file in unit.Files)
        {
            var text = await File.ReadAllTextAsync(file, cancellationToken);
            lines += text.AsSpan().Count('\n');
            trees.Add(CSharpSyntaxTree.ParseText(text, ParseOptions, file, cancellationToken: cancellationToken));
        }

        var parse = watch.Elapsed;

        var compilation = CSharpCompilation.Create(
            shipped ? unit.Name : "Consumer." + unit.Name,
            trees,
            References(unit.Name),
            new CSharpCompilationOptions(
                shipped ? OutputKind.DynamicallyLinkedLibrary : OutputKind.ConsoleApplication,
                nullableContextOptions: NullableContextOptions.Enable,
                allowUnsafe: true));

        var failures = new List<string>();

        watch.Restart();
        var driver = CSharpGeneratorDriver.Create(
            [new ToolRegistrationGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var generatorDiagnostics, cancellationToken);
        var generator = watch.Elapsed;

        foreach (var result in driver.GetRunResult().Results)
        {
            if (result.Exception is not null)
            {
                failures.Add($"generator {result.Generator.GetType().Name} threw: {result.Exception}");
            }
        }

        failures.AddRange(generatorDiagnostics
            .Where(static diagnostic => string.Equals(diagnostic.Id, "CS8785", StringComparison.Ordinal))
            .Select(static diagnostic => diagnostic.ToString()));

        var analyzer = new TraconUsageAnalyzer();
        var options = new AnalyzerOptions(
            [.. additionalFiles.Select(static file => (AdditionalText)new AnalyzerTestHelper.InMemoryAdditionalText(file.Path, file.Text))],
            new AnalyzerTestHelper.InMemoryOptionsProvider(AnalyzerTestHelper.LocalReferenceOn));
        var thrown = new List<string>();
        var withAnalyzers = generated.WithAnalyzers(
            [analyzer],
            new CompilationWithAnalyzersOptions(
                options,
                onAnalyzerException: (exception, source, _) => thrown.Add($"{source.GetType().Name} threw: {exception}"),
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: true,
                reportSuppressedDiagnostics: false));

        watch.Restart();
        var analysis = await withAnalyzers.GetAnalysisResultAsync(cancellationToken);
        var analyzerTime = watch.Elapsed;
        var telemetry = await withAnalyzers.GetAnalyzerTelemetryInfoAsync(analyzer, cancellationToken);

        var diagnostics = analysis.GetAllDiagnostics();
        failures.AddRange(thrown);
        failures.AddRange(diagnostics
            .Where(static diagnostic => diagnostic.Id is "AD0001" or "AD0002")
            .Select(static diagnostic => diagnostic.ToString()));

        // The tool generator's own diagnostics (TRC0001-TRC0012) come from the generator run, not from the analyzer.
        var findings = diagnostics
            .Concat(generatorDiagnostics)
            .Where(static diagnostic => diagnostic.Id.StartsWith("TRC", StringComparison.Ordinal))
            .Select(static diagnostic =>
            {
                var span = diagnostic.Location.GetLineSpan();

                return new CorpusFinding(
                    diagnostic.Id,
                    span.Path,
                    span.StartLinePosition.Line + 1,
                    diagnostic.GetMessage(CultureInfo.InvariantCulture));
            })
            .ToImmutableArray();

        var semanticErrors = countSemanticErrors
            ? generated.GetDiagnostics(cancellationToken).Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            : -1;

        return new UnitReport(
            unit,
            lines,
            parse,
            generator,
            analyzerTime,
            telemetry.ExecutionTime,
            telemetry.CompilationActionsCount + telemetry.CompilationStartActionsCount + telemetry.SyntaxNodeActionsCount
                + telemetry.OperationActionsCount + telemetry.SymbolActionsCount + telemetry.SyntaxTreeActionsCount
                + telemetry.AdditionalFileActionsCount + telemetry.CodeBlockActionsCount + telemetry.CodeBlockStartActionsCount,
            findings,
            [.. failures],
            semanticErrors);
    }

    private static ImmutableArray<MetadataReference> References(string unitName)
    {
        var own = unitName + ".dll";

        return [.. AnalyzerTestHelper.References.Where(reference =>
            reference is not PortableExecutableReference portable
            || !string.Equals(Path.GetFileName(portable.FilePath), own, StringComparison.OrdinalIgnoreCase))];
    }
}
