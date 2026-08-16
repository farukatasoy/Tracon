using System.Diagnostics;
using System.Text;

namespace AgentPrism.Templates.Tests.Infrastructure;

/// <summary>Runs a CLI command as a subprocess and collects its output.</summary>
/// <remarks>
/// 🚨 <strong>MSBuild node reuse is DISABLED here, and this is not a
/// performance setting — it is a DEADLOCK fix.</strong>
/// <para>
/// Measured (2026-08-07): <c>dotnet pack</c>/<c>build</c> starts MSBuild worker
/// nodes with <c>nodeReuse:true</c>, and those nodes <strong>keep living</strong>
/// after the command finishes (~15 minutes by default). The nodes inherit the
/// parent's REDIRECTED stdout/stderr handles; the pipe therefore never sees
/// EOF. .NET's <see cref="Process.WaitForExitAsync"/> call waits not only for
/// the exit code but also for the async readers to FINISH — meaning this
/// method stays blocked until the nodes die, even though the subprocess itself
/// exited within seconds.
/// </para>
/// <para>
/// Symptom: <c>dotnet test</c> hangs for tens of minutes without running any
/// test, <c>ps</c> output shows not a single <c>dotnet pack</c> process, and
/// only orphaned (<c>ppid = 1</c>) <c>MSBuild.dll … /nodeReuse:true</c> nodes
/// remain. Once the nodes are killed, the fixture resumes IMMEDIATELY.
/// </para>
/// </remarks>
internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? RepoPaths.Root,
        };

        // 🚨 See the type's remarks: without this line, RunAsync stays blocked
        // until the leftover MSBuild nodes close the pipe (~15 min), even after
        // the subprocess has exited. The command-line switch (`-nodeReuse:false`)
        // is not enough: commands that INDIRECTLY invoke MSBuild, like
        // `dotnet new`, cannot carry it.
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdOut.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdErr.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = timeout is { } t ? new CancellationTokenSource(t) : new CancellationTokenSource();

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            throw new TimeoutException(
                $"'{fileName} {arguments}' did not finish within {timeout}.{Environment.NewLine}stdout:{stdOut}{Environment.NewLine}stderr:{stdErr}");
        }

        return new ProcessResult(process.ExitCode, stdOut.ToString(), stdErr.ToString());
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process may have already exited - a race condition, ignored.
        }
    }
}
