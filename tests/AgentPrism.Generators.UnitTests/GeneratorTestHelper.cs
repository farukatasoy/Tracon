using System.Collections.Immutable;
using AgentPrism.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AgentPrism.Generators.UnitTests;

/// <summary>Ureteci sentetik bir derleme uzerinde calistiran test yardimcisi.</summary>
internal static class GeneratorTestHelper
{
    private static readonly ImmutableArray<MetadataReference> References = BuildReferences();

    /// <summary>Verilen kaynagi <see cref="ToolRegistrationGenerator"/>'den gecirir.</summary>
    public static GeneratorRunResult Run(string source)
        => RunWithDriver(CreateDriver(), source);

    /// <summary>
    /// Ureteci once <paramref name="firstSource"/>, sonra AYNI SURUCUYLE
    /// (onbellek korunarak) <paramref name="secondSource"/> uzerinde calistirir.
    /// Artimlilik (52.5, <c>IIncrementalGenerator</c> sozlesmesi) bu ikinci kosumun
    /// adim onbellek nedenlerine bakilarak dogrulanir.
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
            "AgentPrism.Generators.Tests.Subject",
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

        builder.Add(MetadataReference.CreateFromFile(typeof(AgentPrismToolAttribute).Assembly.Location));
        builder.Add(MetadataReference.CreateFromFile(typeof(IAgentPrismBuilder).Assembly.Location));

        return builder.ToImmutable();
    }
}

/// <summary>Bir ureteç kosumunun sonucu.</summary>
internal sealed record GeneratorRunResult(
    CSharpCompilation InputCompilation,
    CSharpCompilation OutputCompilation,
    ImmutableArray<Diagnostic> Diagnostics,
    GeneratorDriverRunResult RunResult)
{
    /// <summary>Uretilen tum kaynak dosyalarinin metnini (dosya adi -&gt; icerik) doner.</summary>
    public IReadOnlyDictionary<string, string> GeneratedFiles()
        => RunResult.Results
            .SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);

    /// <summary>Yalnizca belirli bir tanı kimligine (ornek: APG0001) sahip tanilari doner.</summary>
    public IReadOnlyList<Diagnostic> DiagnosticsWithId(string id)
        => [.. Diagnostics.Where(d => string.Equals(d.Id, id, StringComparison.Ordinal))];

    /// <summary>Aggregator disindaki TEK wrapper dosyasinin metnini doner (tek-tool testleri icin).</summary>
    public string SingleWrapperFile()
        => GeneratedFiles().Single(kv => !string.Equals(kv.Key, "AgentPrismGeneratedTools.g.cs", StringComparison.Ordinal)).Value;

    /// <summary>
    /// <paramref name="trackingName"/> ile isaretlenmis adimin bu kosumdaki onbellek
    /// nedenlerini (<see cref="IncrementalStepRunReason"/>) doner.
    /// </summary>
    public IReadOnlyList<IncrementalStepRunReason> StepReasons(string trackingName)
        => [.. RunResult.Results
            .SelectMany(r => r.TrackedSteps.TryGetValue(trackingName, out var steps) ? steps : [])
            .SelectMany(step => step.Outputs.Select(output => output.Reason))];
}
