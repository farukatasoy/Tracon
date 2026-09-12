using System.Globalization;
using System.Text.RegularExpressions;
using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// 130's package-boundary DoD row: a constrained tool's schema, and the run
/// that calls it, both work from a real <c>PackageReference</c> consumer -
/// not just the in-solution generator unit tests, which never cross the
/// packed <c>analyzers/dotnet/cs/</c> boundary. Companion to
/// <see cref="ConsumerRunTests"/>, which proves the same for an unconstrained
/// tool.
/// </summary>
public sealed class ConstrainedToolPackageTests(TemplateFixture fixture)
{
    private static readonly Regex OkLine = new(
        @"^OK run=(?<id>\S+) events=(?<events>\d+) tools=(?<tools>\d+)\r?$",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(1));

    [Fact]
    public async Task Package_consumer_gets_the_constrained_schema_and_the_run_completes()
    {
        using var dir = new TempDirectory();

        await ConstrainedToolConsumerProject.WriteAsync(fixture.Version, dir.Path);

        var buildResult = await ProcessRunner.RunAsync("dotnet", "build -c Release", dir.Path, TimeSpan.FromMinutes(5));
        buildResult.ExitCode.ShouldBe(0, buildResult.Combined);

        var runResult = await ProcessRunner.RunAsync("dotnet", "run -c Release --no-build", dir.Path, TimeSpan.FromMinutes(2));
        runResult.ExitCode.ShouldBe(0, runResult.Combined);

        var match = OkLine.Match(runResult.Combined);
        match.Success.ShouldBeTrue($"Expected an '{ConstrainedToolConsumerProject.ExitedOkPrefix}...' line in stdout:{Environment.NewLine}{runResult.Combined}");

        var eventCount = int.Parse(match.Groups["events"].Value, CultureInfo.InvariantCulture);
        var toolCount = int.Parse(match.Groups["tools"].Value, CultureInfo.InvariantCulture);

        eventCount.ShouldBeGreaterThan(0, runResult.Combined);
        toolCount.ShouldBe(1, runResult.Combined);

        runResult.Combined.ShouldContain("\"minimum\":1", Case.Sensitive, runResult.Combined);
        runResult.Combined.ShouldContain("\"maximum\":10", Case.Sensitive, runResult.Combined);
    }
}
