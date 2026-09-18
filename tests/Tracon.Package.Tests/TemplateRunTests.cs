using System.Diagnostics;
using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// The default (in-memory) combination must start up without any setup, and the
/// catalog endpoint must return 200 (Phase 37 Completion Criteria).
/// </summary>
public sealed class TemplateRunTests(TemplateFixture fixture)
{
    private const string ProjectName = "Run.Sample";
    private const int Port = 5185;

    [Fact]
    public async Task Default_combination_starts_up_without_setup_and_returns_the_catalog()
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync(ProjectName, dir.Path, "--persistence memory --ui false");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        var buildResult = await ProcessRunner.RunAsync("dotnet", "build -c Release", dir.Path, TimeSpan.FromMinutes(5));
        buildResult.ExitCode.ShouldBe(0, buildResult.Combined);

        var dllPath = Directory.EnumerateFiles(dir.Path, $"{ProjectName}.dll", SearchOption.AllDirectories)
                .FirstOrDefault(p => p.Contains(Path.Combine("bin", "Release"), StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"'{ProjectName}.dll' was not found in the build output.{Environment.NewLine}{buildResult.Combined}");

        var startInfo = new ProcessStartInfo("dotnet", $"\"{dllPath}\"")
        {
            WorkingDirectory = dir.Path,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{Port}";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Process.Start returned null for 'dotnet'.");

        try
        {
            using var httpClient = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{Port}/") };

            HttpResponseMessage? response = null;
            var deadline = DateTime.UtcNow.AddSeconds(30);

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    response = await httpClient.GetAsync("tracon/api/agents");
                    break;
                }
                catch (HttpRequestException)
                {
                    if (process.HasExited)
                    {
                        var stdOut = await process.StandardOutput.ReadToEndAsync();
                        var stdErr = await process.StandardError.ReadToEndAsync();
                        throw new InvalidOperationException(
                            $"The application exited unexpectedly (code {process.ExitCode}).{Environment.NewLine}{stdOut}{Environment.NewLine}{stdErr}");
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                }
            }

            if (response is null)
            {
                throw new InvalidOperationException("The application did not respond within 30 seconds.");
            }

            response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10_000);
            }
        }
    }

    /// <summary>
    /// With a provider key set and the model-name placeholder still in place,
    /// the generated app stops at startup and names the file to edit.
    /// </summary>
    /// <remarks>
    /// The placeholder is deliberate, but a provider cannot serve it, so the
    /// first run used to come back as "The model provider request failed." -
    /// the provider's own 404 names the model and that text is a foreign SDK
    /// message, redacted before it reaches the client. The consumer was left
    /// with an error naming nothing on their very first run.
    /// <para>
    /// The guard is conditional on a provider being configured, and the test
    /// above is the other half of the contract: with no key the same app still
    /// starts and still answers.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Unedited_model_placeholder_stops_startup_once_a_provider_is_configured()
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync(
            "Run.Placeholder",
            dir.Path,
            "--persistence memory --ui false --provider openai");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        var buildResult = await ProcessRunner.RunAsync("dotnet", "build -c Release", dir.Path, TimeSpan.FromMinutes(5));
        buildResult.ExitCode.ShouldBe(0, buildResult.Combined);

        var dllPath = Directory.EnumerateFiles(dir.Path, "Run.Placeholder.dll", SearchOption.AllDirectories)
                .FirstOrDefault(path => path.Contains(Path.Combine("bin", "Release"), StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"'Run.Placeholder.dll' was not found in the build output.{Environment.NewLine}{buildResult.Combined}");

        var startInfo = new ProcessStartInfo("dotnet", $"\"{dllPath}\"")
        {
            WorkingDirectory = dir.Path,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        startInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{Port + 1}";

        // A syntactically valid key that is never sent anywhere: startup stops
        // before the first model call.
        startInfo.Environment["Tracon__Providers__OpenAI__ApiKey"] = "sk-not-a-real-key";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Process.Start returned null for 'dotnet'.");

        var stdOut = await process.StandardOutput.ReadToEndAsync();
        var stdErr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        process.ExitCode.ShouldNotBe(0, $"{stdOut}{Environment.NewLine}{stdErr}");

        var output = stdOut + stdErr;

        output.ShouldContain("Program.cs");
        output.ShouldContain("placeholder");
    }
}
