using System.Collections.Concurrent;
using System.Text;

namespace Tracon.Package.Tests.Infrastructure;

/// <summary>Which packages a generated consumer of the public surface references.</summary>
internal enum SurfaceKind
{
    /// <summary>The packages that promise Native AOT. A member that needs unreferenced or dynamic code is left out: the warning on a call is its contract.</summary>
    Aot,

    /// <summary>Every runtime package. No AOT promise, so no trim or AOT analyzer.</summary>
    Full,

    /// <summary>
    /// The AOT packages, with the calls the AOT surface leaves out written too. A negative control: the consumer has
    /// to fail on them, or the analyzers it claims to run are not running.
    /// </summary>
    AotWithContractCalls,
}

/// <summary>The output of one run of the generator.</summary>
/// <param name="Source">The C# file.</param>
/// <param name="Report">The first line is the counts; the others are skipped members and exposures to experimental types.</param>
internal sealed record GeneratedSurface(string Source, string Report)
{
    /// <summary>The counts the generator printed, by name.</summary>
    public IReadOnlyDictionary<string, int> Counts { get; } = Report
        .ReplaceLineEndings("\n")
        .Split('\n')[0]
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(static pair => pair.Split('='))
        .ToDictionary(static pair => pair[0], static pair => int.Parse(pair[1], System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal);

    /// <summary>The lines of the report after the counts.</summary>
    public IReadOnlyList<string> Lines => [.. Report.ReplaceLineEndings("\n").Split('\n').Skip(1).Where(static line => line.Length != 0)];
}

/// <summary>
/// Reads the packed packages by reflection and writes a consumer that touches their whole registration, builder and
/// option surface (see <c>SurfaceGenerator/Program.cs</c>), then writes the strict project that compiles it.
/// </summary>
/// <remarks>
/// The generator is an ordinary console project that references the packed packages, so every dependency resolves the way
/// it does for a consumer and the reflection sees the shipped assemblies, not the ones in the solution.
/// </remarks>
internal static class PublicSurfaceProject
{
    private static readonly TimeSpan GeneratorTimeout = TimeSpan.FromMinutes(10);

    private static readonly ConcurrentDictionary<(string Version, SurfaceKind Kind), Lazy<Task<GeneratedSurface>>> Cache = new();

    private static readonly string[] AotPackages =
    [
        "Tracon.Abstractions",
        "Tracon.Core",
        "Tracon.PostgreSql",
        "Tracon.OpenAI",
        "Tracon.Azure",
        "Tracon.Voice",
    ];

    private static readonly string[] FullPackages =
    [
        "Tracon.Abstractions",
        "Tracon.Core",
        "Tracon.OpenAI",
        "Tracon.AspNetCore",
        "Tracon.Workflows",
        "Tracon.Mcp",
        "Tracon.UI",
        "Tracon.AGUI",
        "Tracon.Anthropic",
        "Tracon.Azure",
        "Tracon.Client",
        "Tracon.Google",
        "Tracon.PostgreSql",
        "Tracon.SqlServer",
        "Tracon.Sqlite",
        "Tracon.Testing",
        "Tracon.Voice",
    ];

    /// <summary>The packages whose assemblies the surface of a kind is read from.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The package ids.</returns>
    public static IReadOnlyList<string> Packages(SurfaceKind kind) => kind == SurfaceKind.Full ? FullPackages : AotPackages;

    /// <summary>Runs the generator once per version and kind and returns the result to every caller.</summary>
    /// <param name="version">The packed version of every Tracon package.</param>
    /// <param name="kind">The kind.</param>
    /// <returns>The generated file and its report.</returns>
    public static Task<GeneratedSurface> GenerateAsync(string version, SurfaceKind kind)
        => Cache.GetOrAdd((version, kind), key => new Lazy<Task<GeneratedSurface>>(() => RunGeneratorAsync(key.Version, key.Kind))).Value;

    private static async Task<GeneratedSurface> RunGeneratorAsync(string version, SurfaceKind kind)
    {
        using var directory = new TempDirectory();
        var packages = Packages(kind);

        await TemplateFixture.WriteLocalNuGetConfigAsync(directory.Path);

        var references = string.Join(
            Environment.NewLine + "    ",
            packages.Select(package => $"<PackageReference Include=\"{package}\" Version=\"{version}\" />"));

        await File.WriteAllTextAsync(Path.Combine(directory.Path, "Generator.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk.Web">

              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <InvariantGlobalization>true</InvariantGlobalization>
              </PropertyGroup>

              <ItemGroup>
                {references}
              </ItemGroup>

            </Project>
            """);

        File.Copy(
            Path.Combine(RepoPaths.Root, "tests", "Tracon.Package.Tests", "SurfaceGenerator", "Program.cs"),
            Path.Combine(directory.Path, "Program.cs"));

        var source = Path.Combine(directory.Path, "Surface.cs");
        var report = Path.Combine(directory.Path, "Surface.report.txt");
        var mode = kind == SurfaceKind.Aot ? "aot" : "full";

        var run = await ProcessRunner.RunAsync(
            "dotnet",
            $"run -c Release --no-launch-profile -- \"{source}\" \"{report}\" {string.Join(',', packages)} {mode}",
            directory.Path,
            GeneratorTimeout);

        if (run.ExitCode != 0 || !File.Exists(source))
        {
            throw new InvalidOperationException("The surface generator failed:" + Environment.NewLine + run.Combined);
        }

        return new GeneratedSurface(await File.ReadAllTextAsync(source), await File.ReadAllTextAsync(report));
    }

    /// <summary>
    /// Writes a consumer that compiles the generated surface under every strictness switch: warnings as errors for the
    /// compiler and for MSBuild, every analyzer category, documentation file, nullable on and no implicit usings.
    /// </summary>
    /// <param name="version">The packed version of every Tracon package.</param>
    /// <param name="directory">An existing, empty directory.</param>
    /// <param name="kind">The kind: it picks the packages, the SDK and whether the trim and AOT analyzers run.</param>
    /// <param name="surface">The generated surface.</param>
    /// <param name="langVersion">The <c>LangVersion</c> to pin, or <see langword="null"/> for the SDK default.</param>
    /// <returns>A task that completes when the files are written.</returns>
    public static async Task WriteConsumerAsync(string version, string directory, SurfaceKind kind, GeneratedSurface surface, string? langVersion = null)
    {
        await TemplateFixture.WriteLocalNuGetConfigAsync(directory);

        var aot = kind != SurfaceKind.Full;
        var references = string.Join(
            Environment.NewLine + "    ",
            Packages(kind).Select(package => $"<PackageReference Include=\"{package}\" Version=\"{version}\" />"));

        var properties = new StringBuilder();

        if (aot)
        {
            properties.AppendLine("    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>");
            properties.AppendLine("    <EnableAotAnalyzer>true</EnableAotAnalyzer>");
            properties.AppendLine("    <EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>");
            properties.AppendLine("    <IsTrimmable>true</IsTrimmable>");
        }

        if (langVersion is not null)
        {
            properties.AppendLine("    <LangVersion>" + langVersion + "</LangVersion>");
        }

        // An AOT consumer is a plain console host: it needs the hosting package the packages declare no dependency on.
        var hosting = aot ? $"    <PackageReference Include=\"Microsoft.Extensions.Hosting\" Version=\"{StrictConsumerProject.HostingVersion}\" />\n" : string.Empty;

        await File.WriteAllTextAsync(Path.Combine(directory, "Consumer.csproj"), $"""
            <Project Sdk="{(aot ? "Microsoft.NET.Sdk" : "Microsoft.NET.Sdk.Web")}">

              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>disable</ImplicitUsings>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <MSBuildTreatWarningsAsErrors>true</MSBuildTreatWarningsAsErrors>
                <EnableNETAnalyzers>true</EnableNETAnalyzers>
                <AnalysisLevel>latest-all</AnalysisLevel>
                <AnalysisMode>All</AnalysisMode>
                <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
                <GenerateDocumentationFile>true</GenerateDocumentationFile>
                <InvariantGlobalization>true</InvariantGlobalization>
            {properties}  </PropertyGroup>

              <ItemGroup>
                {references}
            {hosting}  </ItemGroup>

            </Project>
            """);

        await File.WriteAllTextAsync(Path.Combine(directory, "Surface.cs"), surface.Source);

        // Main reaches every generated method through a branch the compiler cannot decide, so a trimming publish
        // looks at all of them; the program itself never takes it.
        await File.WriteAllTextAsync(Path.Combine(directory, "Program.cs"), """
            namespace Consumer
            {
                /// <summary>Entry point.</summary>
                internal static class Program
                {
                    private static int Main(string[] args)
                    {
                        if (args is { Length: > 100000 })
                        {
                            global::SurfaceProbe.SurfaceRoot.Everything();
                        }

                        return 0;
                    }
                }
            }
            """);
    }
}
