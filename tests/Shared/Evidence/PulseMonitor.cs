using System.Runtime.CompilerServices;

namespace Tracon.Tests.Common;

/// <summary>
/// The test process's heartbeat: one dedicated thread that measures how late it wakes and how long the thread pool takes
/// to start a work item, and writes the delays that matter next to the test results.
/// </summary>
/// <remarks>
/// <para>
/// Linked into every test project by <c>tests/Directory.Build.props</c> and started when the assembly loads. The thread is a
/// background thread and costs ten wake-ups a second. A delay at or above <see cref="Threshold"/> is appended to
/// <c>pulse.txt</c> in <see cref="TestEvidence.EvidenceDirectory"/>; a quiet run leaves no file.
/// </para>
/// <para>
/// <see cref="WaitUntil"/> asks <see cref="Window"/> when a wait times out, so the failure message says whether the process
/// stood still while the wait was running.
/// </para>
/// </remarks>
internal static class PulseMonitor
{
    /// <summary>How often the heartbeat thread wakes.</summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    /// <summary>The delay at or above which a measurement is written.</summary>
    internal static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(250);

    private static readonly Lock Gate = new();
    private static PulseRecorder? _recorder;

    /// <summary>The recorder, once the monitor has started; <see langword="null"/> before that.</summary>
    internal static PulseRecorder? Recorder => Volatile.Read(ref _recorder);

    /// <summary>Starts the heartbeat thread once.</summary>
    [ModuleInitializer]
    internal static void Start()
    {
        lock (Gate)
        {
            if (_recorder is not null)
            {
                return;
            }

            var recorder = new PulseRecorder(TimeProvider.System, Interval, Threshold, WriteLine);
            var thread = new Thread(() => Run(recorder))
            {
                IsBackground = true,
                Name = "test-pulse",
                Priority = ThreadPriority.AboveNormal,
            };

            _recorder = recorder;
            thread.Start();
        }
    }

    /// <summary>
    /// The worst delays since <paramref name="since"/>, or <see langword="null"/> when the monitor never started.
    /// </summary>
    /// <param name="since">A value of <see cref="PulseRecorder.Now"/>.</param>
    internal static PulseWindow? Window(long since) => Recorder?.Window(since);

    private static void Run(PulseRecorder recorder)
    {
        using var stop = new ManualResetEventSlim();

        // Not a sleep: the thread waits on an event nobody sets, which is how a dedicated thread keeps a period.
        while (!stop.Wait(Interval))
        {
            try
            {
                recorder.Beat();

                var probe = recorder.PoolProbeQueued();
                ThreadPool.UnsafeQueueUserWorkItem(static (state) => state.recorder.PoolProbeStarted(state.probe), (recorder, probe), preferLocal: false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Evidence is a courtesy: a full disk must not turn a heartbeat into a failure.
            }
        }
    }

    private static void WriteLine(string line)
    {
        var directory = TestEvidence.EvidenceDirectory;

        if (directory is null)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        File.AppendAllText(Path.Combine(directory, "pulse.txt"), line + Environment.NewLine);
    }
}
