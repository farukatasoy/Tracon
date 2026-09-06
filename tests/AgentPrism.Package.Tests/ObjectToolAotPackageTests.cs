using System.Runtime.InteropServices;
using AgentPrism.Package.Tests.Infrastructure;

namespace AgentPrism.Package.Tests;

/// <summary>
/// 135's DoD: an object parameter (135.1) binds through
/// <c>System.Text.Json.JsonSerializer.Deserialize(JsonElement, JsonTypeInfo)</c>
/// (135.5) - a run-time behavior a generator unit test cannot prove, because it
/// never crosses the packed <c>analyzers/dotnet/cs/</c> boundary and never
/// actually trims. This test does both: it publishes a real
/// <c>PackageReference</c> consumer with <c>PublishAot=true</c> and runs the
/// PUBLISHED, TRIMMED executable, exactly the way an external consumer would
/// deploy one.
/// </summary>
public sealed class ObjectToolAotPackageTests(TemplateFixture fixture)
{
    [Fact]
    public async Task An_object_parameter_tool_publishes_under_Native_AOT_without_a_trim_warning_and_runs()
    {
        using var dir = new TempDirectory();

        await ObjectToolAotConsumerProject.WriteAsync(fixture.Version, dir.Path);

        var rid = RuntimeInformation.RuntimeIdentifier;
        var publishDirectory = Path.Combine(dir.Path, "publish");

        // Restore and publish run as ONE command: a separate `dotnet restore`
        // does not reliably add the RID-specific ILCompiler runtime package to
        // the consumer's package cache (same constraint release_extension_samples.py
        // documents for AgentPrism.Samples.ExtensionAotSmoke).
        var publishResult = await ProcessRunner.RunAsync(
            "dotnet",
            $"publish -c Release -r {rid} -o \"{publishDirectory}\"",
            dir.Path,
            TimeSpan.FromMinutes(10));

        publishResult.ExitCode.ShouldBe(0, publishResult.Combined);
        publishResult.Combined.ShouldNotContain("IL2026", Case.Sensitive, publishResult.Combined);
        publishResult.Combined.ShouldNotContain("IL3050", Case.Sensitive, publishResult.Combined);

        var executable = Path.Combine(publishDirectory, OperatingSystem.IsWindows() ? "Consumer.exe" : "Consumer");
        File.Exists(executable).ShouldBeTrue($"Expected a published executable at '{executable}'.{Environment.NewLine}{publishResult.Combined}");

        var runResult = await ProcessRunner.RunAsync(executable, string.Empty, dir.Path, TimeSpan.FromMinutes(1));

        runResult.ExitCode.ShouldBe(0, runResult.Combined);
        runResult.Combined.ShouldContain(
            $"{ObjectToolAotConsumerProject.ExitedOkPrefix}Clarity:5",
            Case.Sensitive,
            runResult.Combined);
    }
}
