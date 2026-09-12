using System.Diagnostics;
using System.Text;

namespace Tracon.Tests.Common;

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
/// <para>
/// Phase 157 moved this type out of <c>Tracon.Package.Tests</c> and into
/// <c>tests/Shared/Infrastructure</c>, linked by every test project that needs
/// it. <see cref="StartAsync"/> was added there for the two-process failure
/// proof, which needs a subprocess that keeps RUNNING and can be killed —
/// <see cref="RunAsync"/> only covers run-to-completion. A second
/// implementation of the deadlock fix above is exactly the synchronization-copy
/// class this repository has paid for five times, so the two entry points share
/// one <see cref="ProcessStartInfo"/> builder.
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
        var startInfo = CreateStartInfo(fileName, arguments, workingDirectory, environment);

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

    /// <summary>
    /// Starts a long-running subprocess and returns a handle to it, without
    /// waiting for it to exit.
    /// </summary>
    /// <param name="fileName">The executable.</param>
    /// <param name="arguments">The command line.</param>
    /// <param name="readyLine">
    /// A line the process is expected to write to stdout once it is ready. The
    /// returned task completes when that line appears; if the process exits
    /// first, or <paramref name="readyTimeout"/> elapses, this throws.
    /// </param>
    /// <param name="readyTimeout">How long to wait for <paramref name="readyLine"/>.</param>
    /// <param name="workingDirectory">The working directory; the repository root by default.</param>
    /// <param name="environment">Extra environment variables.</param>
    /// <returns>The running process handle.</returns>
    /// <remarks>
    /// The readiness handshake is a CONDITION, not a sleep. A fixed delay is
    /// what makes a process test flaky on a loaded CI agent; a line on stdout
    /// is observable and the timeout only ever guards against a hang.
    /// </remarks>
    public static async Task<ManagedProcess> StartAsync(
        string fileName,
        string arguments,
        string readyLine,
        TimeSpan readyTimeout,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = CreateStartInfo(fileName, arguments, workingDirectory, environment);
        var managed = new ManagedProcess(new Process { StartInfo = startInfo }, $"{fileName} {arguments}");

        try
        {
            await managed.StartAsync(readyLine, readyTimeout);
            return managed;
        }
        catch
        {
            await managed.DisposeAsync();
            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        string fileName,
        string arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? RepoRoot.Path,
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

        return startInfo;
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
