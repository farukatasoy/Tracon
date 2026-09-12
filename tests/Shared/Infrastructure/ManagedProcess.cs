using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Tracon.Tests.Common;

/// <summary>
/// A subprocess that keeps running after it is started, with its output
/// captured and a way to kill it the way an operator's crash would.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="KillAsync"/> is the point of this type. A test that proves what
/// happens when a worker DIES cannot use a graceful stop: a graceful stop lets
/// the host release its lease, which is the opposite of the scenario. On Unix
/// the kill is a real <c>SIGKILL</c>; on Windows <see cref="Process.Kill()"/>
/// terminates without giving the process a chance to run shutdown code, which
/// is the closest equivalent the platform offers.
/// </para>
/// <para>
/// <see cref="DisposeAsync"/> always kills the tree, so a test that fails
/// halfway leaves no orphaned worker holding a database connection behind for
/// the next test class.
/// </para>
/// </remarks>
internal sealed class ManagedProcess(Process process, string description) : IAsyncDisposable
{
    private readonly ConcurrentQueue<string> _output = new();
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? _ready;
    private string? _readyLine;
    private bool _disposed;

    /// <summary>Gets the operating-system process identifier.</summary>
    public int Id { get; private set; }

    /// <summary>Gets everything the process has written to stdout and stderr so far.</summary>
    public string Output => string.Join(Environment.NewLine, _output);

    /// <summary>Gets a value indicating whether the process has exited.</summary>
    public bool HasExited => _exited.Task.IsCompleted;

    internal async Task StartAsync(string readyLine, TimeSpan readyTimeout)
    {
        _readyLine = readyLine;
        _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => _exited.TrySetResult();
        process.OutputDataReceived += (_, e) => Capture(e.Data);
        process.ErrorDataReceived += (_, e) => Capture(e.Data);

        process.Start();
        Id = process.Id;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // The process exiting is a completion of the wait too - otherwise a
        // crash at startup would show up as a timeout, hiding the real error.
        var completed = await Task.WhenAny(
            _ready.Task,
            _exited.Task,
            Task.Delay(readyTimeout));

        if (completed == _ready.Task)
        {
            return;
        }

        var reason = _exited.Task.IsCompleted
            ? FormattableString.Invariant($"it exited with code {process.ExitCode}")
            : FormattableString.Invariant($"it did not print it within {readyTimeout}");

        throw new InvalidOperationException(
            $"'{description}' never reported ready ('{readyLine}'): {reason}.{Environment.NewLine}{Output}");
    }

    /// <summary>Waits until the process writes <paramref name="line"/> to its output.</summary>
    /// <param name="line">The substring to wait for.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>The completion task.</returns>
    public async Task WaitForOutputAsync(string line, TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();

        while (deadline.Elapsed < timeout)
        {
            if (_output.Any(l => l.Contains(line, StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        throw new TimeoutException(
            $"'{description}' did not write '{line}' within {timeout}.{Environment.NewLine}{Output}");
    }

    /// <summary>
    /// Kills the process the way a crash would - no shutdown code runs, no
    /// lease is released.
    /// </summary>
    /// <returns>The completion task.</returns>
    public async Task KillAsync()
    {
        if (HasExited)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            // SIGKILL. Process.Kill() maps to this on Unix already, but going
            // through the syscall makes the intent unmistakable in the test's
            // failure output and keeps the manifest honest about WHICH signal
            // was measured.
            _ = Interop.Kill(process.Id, Interop.Sigkill);
        }
        else
        {
            process.Kill(entireProcessTree: false);
        }

        await _exited.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            }
        }
        catch (InvalidOperationException)
        {
            // Never started, or already reaped.
        }
        catch (TimeoutException)
        {
            // Nothing further this fixture can do; the assertion output already
            // carries the process description.
        }
        finally
        {
            process.Dispose();
        }
    }

    private void Capture(string? data)
    {
        if (data is null)
        {
            return;
        }

        _output.Enqueue(data);

        if (_readyLine is not null && data.Contains(_readyLine, StringComparison.Ordinal))
        {
            _ready?.TrySetResult();
        }
    }

    private static class Interop
    {
        public const int Sigkill = 9;

        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        public static extern int Kill(int pid, int sig);
    }
}
