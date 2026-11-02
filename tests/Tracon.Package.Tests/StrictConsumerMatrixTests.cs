using System.Text;
using System.Text.RegularExpressions;
using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// A consumer that turns every strictness switch on builds against the packed packages with no warning at all.
/// </summary>
/// <remarks>
/// <para>
/// A consumer who treats warnings as errors, runs every analyzer category and manages versions centrally meets a
/// package differently from the one the repository builds: the repository's own settings are strict too, but they are
/// the settings the code was written against. The code in these projects is clean by construction (see
/// <see cref="StrictConsumerProject"/>), so a warning in the build output comes from a Tracon package: an analyzer
/// finding, generated code, a build target, or the dependency graph.
/// </para>
/// <para>
/// The default run is a short list that covers each shape once and each switch at both of its values. The full product
/// of the switches is <see cref="Every_cell_of_the_strict_matrix_builds_without_a_warning"/>: it is explicit, so it runs
/// on request (<c>--explicit only</c>) and not with the suite.
/// </para>
/// <para>
/// Measured on a development machine (10 cores, shared with other work): 5-12 seconds per cell once the packages are in
/// the cache, so the default list adds about a minute and the full product about fifteen.
/// </para>
/// </remarks>
public sealed class StrictConsumerMatrixTests(TemplateFixture fixture)
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(10);

    private static readonly Regex WarningLine = new(
        @"^.*: warning [A-Za-z]+\d+:",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly StrictCell[] DefaultCells =
    [
        new(ConsumerShape.Web, CentralPackages: true, Nullable: true, ImplicitUsings: true, Documentation: true),
        new(ConsumerShape.Web, CentralPackages: false, Nullable: false, ImplicitUsings: false, Documentation: false),
        new(ConsumerShape.Library, CentralPackages: false, Nullable: true, ImplicitUsings: true, Documentation: true),
        new(ConsumerShape.TrimmableConsole, CentralPackages: true, Nullable: true, ImplicitUsings: false, Documentation: true),
        new(ConsumerShape.ContractTests, CentralPackages: false, Nullable: true, ImplicitUsings: true, Documentation: true, OldXunit: true),
    ];

    public static TheoryData<string> DefaultCellNames() => [.. DefaultCells.Select(static cell => cell.Name)];

    [Theory]
    [MemberData(nameof(DefaultCellNames))]
    public async Task A_strict_consumer_builds_without_a_warning(string name)
    {
        var cell = DefaultCells.Single(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));

        var failure = await BuildCellAsync(cell);

        failure.ShouldBeNull();
    }

    /// <summary>
    /// The shipped contract suite also runs on the oldest xunit line its package declares, which the repository's own
    /// tests (all on the current line) never exercise.
    /// </summary>
    [Fact]
    public async Task The_contract_suite_runs_on_the_oldest_supported_xunit_line()
    {
        using var directory = new TempDirectory();
        var cell = DefaultCells.Single(static candidate => candidate.OldXunit);

        await StrictConsumerProject.WriteAsync(fixture.Version, directory.Path, cell);

        var build = await ProcessRunner.RunAsync("dotnet", "build -c Release", directory.Path, BuildTimeout);
        build.ExitCode.ShouldBe(0, build.Combined);

        var executable = Path.Combine(directory.Path, "bin", "Release", "net10.0", OperatingSystem.IsWindows() ? "Consumer.exe" : "Consumer");
        var run = await ProcessRunner.RunAsync(executable, string.Empty, directory.Path, TimeSpan.FromMinutes(2));

        run.ExitCode.ShouldBe(0, run.Combined);
        run.Combined.ShouldContain("v3.2.2", Case.Sensitive, "The run was not on the xunit.v3 floor.\n" + run.Combined);
        run.Combined.ShouldContain("Failed: 0", Case.Sensitive, run.Combined);
        run.Combined.ShouldNotContain("Total: 0,", Case.Sensitive, "No contract test was discovered.\n" + run.Combined);
    }

    /// <summary>
    /// The path of a project carries a space and the letters of a Turkish name, once as precomposed characters and once
    /// as base letters followed by combining marks. The build writes beside the project when a build target is switched
    /// on, so the awkward path is also the path of a file Tracon creates.
    /// </summary>
    /// <param name="form">The Unicode normalization form the directory name is written in.</param>
    [Theory]
    [InlineData(NormalizationForm.FormC)]
    [InlineData(NormalizationForm.FormD)]
    public async Task A_path_with_a_space_and_Turkish_letters_builds_and_receives_the_files_the_build_writes(NormalizationForm form)
    {
        using var directory = new TempDirectory();
        var projectDirectory = Path.Combine(directory.Path, "Çağrı Ö".Normalize(form), "proje");
        Directory.CreateDirectory(projectDirectory);

        await StrictConsumerProject.WriteAsync(
            fixture.Version,
            projectDirectory,
            new StrictCell(ConsumerShape.Web, CentralPackages: false, Nullable: true, ImplicitUsings: true, Documentation: true));

        var project = Path.Combine(projectDirectory, "Consumer.csproj");
        await File.WriteAllTextAsync(
            project,
            (await File.ReadAllTextAsync(project, TestContext.Current.CancellationToken))
                .Replace("</PropertyGroup>", "<TraconWriteAgentsFile>true</TraconWriteAgentsFile></PropertyGroup>", StringComparison.Ordinal),
            TestContext.Current.CancellationToken);

        var build = await ProcessRunner.RunAsync("dotnet", "build -c Release", projectDirectory, BuildTimeout);

        build.ExitCode.ShouldBe(0, build.Combined);
        WarningLine.IsMatch(build.Combined).ShouldBeFalse(build.Combined);
        File.Exists(Path.Combine(projectDirectory, "AGENTS.md")).ShouldBeTrue(build.Combined);
        File.Exists(Path.Combine(projectDirectory, "Tracon.LocalReference.md")).ShouldBeTrue(build.Combined);
    }

    /// <summary>
    /// The product of every switch over every shape. It builds a few dozen projects, so it runs on request:
    /// <c>--explicit only --filter-method "*Every_cell_of_the_strict_matrix*"</c>.
    /// </summary>
    [Fact(Explicit = true)]
    public async Task Every_cell_of_the_strict_matrix_builds_without_a_warning()
    {
        var failures = new List<string>();

        foreach (var cell in AllCells())
        {
            var failure = await BuildCellAsync(cell);

            if (failure is not null)
            {
                failures.Add(failure);
            }
        }

        failures.ShouldBeEmpty();
    }

    private static IEnumerable<StrictCell> AllCells()
    {
        bool[] both = [false, true];

        foreach (var shape in Enum.GetValues<ConsumerShape>())
        {
            foreach (var central in both)
            {
                foreach (var nullable in both)
                {
                    foreach (var usings in both)
                    {
                        foreach (var documentation in both)
                        {
                            foreach (var oldXunit in shape == ConsumerShape.ContractTests ? both : [false])
                            {
                                yield return new StrictCell(shape, central, nullable, usings, documentation, oldXunit);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>Builds one cell and returns a description of what went wrong, or <see langword="null"/>.</summary>
    private async Task<string?> BuildCellAsync(StrictCell cell)
    {
        using var directory = new TempDirectory();

        await StrictConsumerProject.WriteAsync(fixture.Version, directory.Path, cell);

        var build = await ProcessRunner.RunAsync("dotnet", "build -c Release", directory.Path, BuildTimeout);

        if (build.ExitCode == 0 && !WarningLine.IsMatch(build.Combined))
        {
            return null;
        }

        var lines = build.Combined
            .Split('\n')
            .Where(static line => line.Contains(": warning ", StringComparison.Ordinal) || line.Contains(": error ", StringComparison.Ordinal))
            .Select(static line => line.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(12);

        return $"{cell.Name}: exit {build.ExitCode}{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }
}
