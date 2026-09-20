using System.Text.RegularExpressions;
using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// Verifies that the template actually compiles at at least two endpoints: the
/// most minimal (in-memory, no UI) and the most fully loaded (SQL Server +
/// Azure OpenAI + UI) combination. The template can break silently when the
/// library changes (37.3); these tests are the only real gate that catches
/// that breakage.
/// </summary>
public sealed class TemplateInstantiationTests(TemplateFixture fixture)
{
    private static readonly Regex WarningLine = new(
        @"^.*: warning [A-Z]+\d+:",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(1));

    [Fact]
    public async Task Default_package_version_matches_the_packed_template_version()
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync("Default.Version.Sample", dir.Path);
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        var project = await File.ReadAllTextAsync(
            Path.Combine(dir.Path, "Default.Version.Sample.csproj"));

        project.ShouldContain($"Version=\"{fixture.Version}\"");
        project.ShouldNotContain("TRACON_TEMPLATE_PACKAGE_VERSION");
    }

    [Fact]
    public async Task Most_minimal_combination_compiles_with_zero_warnings()
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync("Minimal.Sample", dir.Path, "--persistence memory --provider openai --ui false");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        var buildResult = await ProcessRunner.RunAsync("dotnet", "build -c Release", dir.Path, TimeSpan.FromMinutes(5));

        buildResult.ExitCode.ShouldBe(0, buildResult.Combined);
        WarningLine.IsMatch(buildResult.Combined).ShouldBeFalse(buildResult.Combined);
    }

    [Fact]
    public async Task Most_full_combination_compiles_with_zero_warnings()
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync(
            "Full.Sample",
            dir.Path,
            "--persistence sqlserver --provider azure --ui true");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        var buildResult = await ProcessRunner.RunAsync("dotnet", "build -c Release", dir.Path, TimeSpan.FromMinutes(5));

        buildResult.ExitCode.ShouldBe(0, buildResult.Combined);
        WarningLine.IsMatch(buildResult.Combined).ShouldBeFalse(buildResult.Combined);
    }
}
