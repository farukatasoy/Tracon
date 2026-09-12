
namespace Tracon.Core.UnitTests.Skills;

/// <summary>
/// Verifies the boundaries of the layer that actually launches the process.
/// </summary>
/// <remarks>
/// Tests use <c>bash</c>; they are skipped on platforms where it is missing.
/// Script files are produced in a temporary folder; no executable file is
/// added to the repository.
/// </remarks>
public sealed class SkillScriptProcessRunnerTests : IDisposable
{
    private static readonly string? BashPath = FindBash();

    private readonly string _root = Directory.CreateTempSubdirectory("tracon-tests-").FullName;

    [Fact]
    public async Task Environment_variables_do_not_leak_into_the_process()
    {
        Assert.SkipWhen(BashPath is null, "bash not found.");

        // A variable that is not on the whitelist must not reach the script,
        // even if it is defined in the process at run time.
        Environment.SetEnvironmentVariable("TRACON_TEST_CONNECTIONSTRING", "secret-value");
        try
        {
            var script = WriteScript("env.sh", "echo \"[$TRACON_TEST_CONNECTIONSTRING]\"");

            var result = await RunAsync(script);

            result.StandardOutput.ShouldNotContain("secret-value");
            result.StandardOutput.ShouldContain("[]");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRACON_TEST_CONNECTIONSTRING", null);
        }
    }

    [Fact]
    public async Task Timeout_kills_the_process()
    {
        Assert.SkipWhen(BashPath is null, "bash not found.");
        var script = WriteScript("sleep.sh", "sleep 30");

        var result = await RunAsync(script, options => options.Timeout = TimeSpan.FromMilliseconds(300));

        result.TimedOut.ShouldBeTrue();
        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Output_is_truncated_once_the_limit_is_exceeded()
    {
        Assert.SkipWhen(BashPath is null, "bash not found.");
        var script = WriteScript("flood.sh", "for i in $(seq 1 5000); do echo 0123456789; done");

        var result = await RunAsync(script, options => options.MaxOutputBytes = 512);

        result.Truncated.ShouldBeTrue();
        result.StandardOutput.Length.ShouldBeLessThanOrEqualTo(1024);
    }

    [Fact]
    public async Task Temp_directory_variable_is_writable_and_deleted_after_the_run()
    {
        Assert.SkipWhen(BashPath is null, "bash not found.");
        var script = WriteScript(
            "temp.sh",
            $"echo data > \"${SkillScriptProcessRunner.TempDirectoryVariable}/x.txt\" && echo \"${SkillScriptProcessRunner.TempDirectoryVariable}\"");

        var result = await RunAsync(script);

        result.Succeeded.ShouldBeTrue();
        Directory.Exists(result.StandardOutput.Trim()).ShouldBeFalse();
    }

    [Fact]
    public async Task Script_exiting_without_reading_stdin_does_not_throw_a_broken_pipe_exception()
    {
        // HATA-K-skill-pipe (2026-08-15): when a script exits without ever
        // reading stdin (e.g. a bare 'echo'), the failure did not happen while
        // writing arguments, but inside StandardInput.Close()'s OWN internal
        // flush, which threw "Pipe is broken"; the finally block did not catch
        // it and the run crashed entirely (the exception leaked to the
        // caller). This test deterministically triggers it with a script that
        // DELIBERATELY closes stdin early.
        Assert.SkipWhen(BashPath is null, "bash not found.");
        var script = WriteScript("closestdin.sh", "exec 0<&-\necho done");

        var result = await RunAsync(script, argumentsJson: "{\"arguments\":\"\"}");

        result.Succeeded.ShouldBeTrue();
        result.StandardOutput.ShouldContain("done");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A cleanup failure must not change the test result.
        }
    }

    private static string? FindBash()
    {
        foreach (var candidate in new[] { "/bin/bash", "/usr/bin/bash" })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private string WriteScript(string name, string body)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, body + Environment.NewLine);
        return path;
    }

    private Task<SkillScriptExecutionResult> RunAsync(
        string scriptPath,
        Action<TraconSkillScriptOptions>? configure = null,
        string? argumentsJson = null)
    {
        var options = new TraconOptions().Skills.Scripts;
        options.Enabled = true;
        options.PlatformIsolationAcknowledged = true;
        options.Interpreters["sh"] = BashPath!;
        configure?.Invoke(options);

        return SkillScriptProcessRunner.ExecuteAsync(
            BashPath!,
            scriptPath,
            _root,
            argumentsJson,
            options,
            skillName: "test",
            TimeProvider.System,
            CancellationToken.None);
    }
}
