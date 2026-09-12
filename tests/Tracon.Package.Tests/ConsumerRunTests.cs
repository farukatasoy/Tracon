using System.Globalization;
using System.Text.RegularExpressions;
using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// The one gate that proves a real <c>PackageReference</c> consumer can run
/// an agent end to end: a generated tool executes, the run record is written,
/// and at least one <c>run_event</c> is produced (Phase 95, item 10).
/// </summary>
/// <remarks>
/// The run's own assertions (<c>ShouldHaveCompleted</c>,
/// <c>ShouldHaveCalledTool</c>, <c>ShouldHaveOutputContaining</c>) execute
/// INSIDE the generated consumer project's <c>Program.cs</c> - see
/// <see cref="ConsumerProject"/> for why. If any of them fail, the process
/// exits with a non-zero code and the exception text lands in stderr; this
/// test does not repeat those assertions, it verifies the process actually
/// reached and printed the success line.
/// </remarks>
public sealed class ConsumerRunTests(TemplateFixture fixture)
{
    // '\r?$', not a bare '$': in multiline mode .NET anchors '$' BEFORE the
    // '\n', so on Windows the carriage return of the consumer process's CRLF
    // output sits between the last digit and the anchor and the match fails on
    // a line that is plainly there (measured on the windows-latest CI leg; the
    // failure message printed the very line it could not match).
    private static readonly Regex OkLine = new(
        @"^OK run=(?<id>\S+) events=(?<events>\d+) tools=(?<tools>\d+)\r?$",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(1));

    [Fact]
    public async Task Package_consumer_runs_an_agent_and_the_generated_tool_executes()
    {
        using var dir = new TempDirectory();

        await ConsumerProject.WriteAsync(fixture.Version, dir.Path);

        var buildResult = await ProcessRunner.RunAsync("dotnet", "build -c Release", dir.Path, TimeSpan.FromMinutes(5));
        buildResult.ExitCode.ShouldBe(0, buildResult.Combined);

        var runResult = await ProcessRunner.RunAsync("dotnet", "run -c Release --no-build", dir.Path, TimeSpan.FromMinutes(2));
        runResult.ExitCode.ShouldBe(0, runResult.Combined);

        var match = OkLine.Match(runResult.Combined);
        match.Success.ShouldBeTrue($"Expected an '{ConsumerProject.ExitedOkPrefix}...' line in stdout:{Environment.NewLine}{runResult.Combined}");

        var eventCount = int.Parse(match.Groups["events"].Value, CultureInfo.InvariantCulture);
        var toolCount = int.Parse(match.Groups["tools"].Value, CultureInfo.InvariantCulture);

        eventCount.ShouldBeGreaterThan(0, runResult.Combined);
        toolCount.ShouldBe(1, runResult.Combined);
    }
}
