using System.Diagnostics;

namespace Tracon.CapacityDriver;

/// <summary>One sample of one process's resource use, as one line of <c>resources.jsonl</c>.</summary>
public sealed class ResourceSample
{
    /// <summary>The cell being measured.</summary>
    public string CellId { get; set; } = "";

    /// <summary>What the process is: <c>host</c>, <c>driver</c> or <c>worker-N</c>.</summary>
    public string Role { get; set; } = "";

    /// <summary>The operating-system process id.</summary>
    public int Pid { get; set; }

    /// <summary>When the sample was taken, for correlation only.</summary>
    public DateTimeOffset Utc { get; set; }

    /// <summary>Seconds from the window's start.</summary>
    public double OffsetSeconds { get; set; }

    /// <summary>Resident set size, in bytes.</summary>
    public long RssBytes { get; set; }

    /// <summary>Cumulative processor time, in seconds.</summary>
    public double ProcessorSeconds { get; set; }

    /// <summary>Managed heap size, when the process is this one and can report it.</summary>
    public long? ManagedHeapBytes { get; set; }

    /// <summary>Cumulative allocated bytes, when the process is this one.</summary>
    public long? AllocatedBytes { get; set; }

    /// <summary>Cumulative garbage collection time, when the process is this one.</summary>
    public double? GcSeconds { get; set; }
}

/// <summary>Samples several processes at a fixed interval while a cell runs.</summary>
/// <remarks>
/// <para>
/// 🚨 Host, driver and each worker are sampled <em>separately, by pid</em>. A
/// single machine-wide number would make it impossible to tell a saturated
/// server from a saturated client, and telling those apart is the difference
/// between a capacity finding and a driver bug.
/// </para>
/// <para>
/// Managed heap, allocation and garbage-collection time are available only for
/// the driver's own process - the runtime exposes them per process, and the
/// driver cannot read another process's counters. Rather than invent a zero,
/// those fields stay null for other processes and the coverage record says why.
/// </para>
/// </remarks>
public sealed class ResourceSampler : IAsyncDisposable
{
    private readonly List<TrackedProcess> _tracked = [];
    private readonly BoundedJsonlWriter<ResourceSample> _writer;
    private readonly TimeSpan _interval;
    private readonly string _cellId;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private long _windowStartTimestamp;

    /// <summary>Creates a sampler over the given processes.</summary>
    /// <param name="cellId">The cell being measured.</param>
    /// <param name="writer">Where the samples go.</param>
    /// <param name="interval">How often to sample.</param>
    public ResourceSampler(string cellId, BoundedJsonlWriter<ResourceSample> writer, TimeSpan interval)
    {
        _cellId = cellId;
        _writer = writer;
        _interval = interval;
    }

    /// <summary>Adds a process to the sample set.</summary>
    /// <param name="role">What the process is.</param>
    /// <param name="pid">Its process id.</param>
    /// <returns>Whether the process could be opened.</returns>
    public bool Track(string role, int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            _tracked.Add(new TrackedProcess(role, pid, process, IsSelf: pid == Environment.ProcessId));
            return true;
        }
        catch (ArgumentException)
        {
            // The process is gone, or was never there. Nothing is invented for
            // it: the caller records the gap in the telemetry coverage.
            return false;
        }
    }

    /// <summary>The roles that could actually be opened.</summary>
    public IReadOnlyList<string> TrackedRoles => _tracked.ConvertAll(static t => t.Role);

    /// <summary>Starts sampling.</summary>
    public void Start()
    {
        _windowStartTimestamp = Stopwatch.GetTimestamp();

        foreach (var tracked in _tracked)
        {
            tracked.StartProcessorSeconds = SafeProcessorSeconds(tracked);
            tracked.StartAllocatedBytes = tracked.IsSelf ? GC.GetTotalAllocatedBytes() : 0;
            tracked.StartGcSeconds = tracked.IsSelf ? GC.GetTotalPauseDuration().TotalSeconds : (double?)null;
        }

        _loop = Task.Run(LoopAsync);
    }

    /// <summary>The highest resident set each tracked process reached.</summary>
    public Dictionary<string, long> PeakRssBytes { get; } = [];

    /// <summary>Stops sampling and reduces the series.</summary>
    /// <returns>One summary per tracked process.</returns>
    public async Task<List<ProcessResourceSummary>> StopAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);

        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                // Stopping the sampler is the expected way this loop ends.
            }
        }

        var summaries = new List<ProcessResourceSummary>(_tracked.Count);

        foreach (var tracked in _tracked)
        {
            summaries.Add(new ProcessResourceSummary
            {
                Role = tracked.Role,
                Pid = tracked.Pid,
                Samples = tracked.Samples,
                PeakRssBytes = tracked.PeakRss,
                MeanRssBytes = tracked.Samples == 0 ? 0 : tracked.RssTotal / tracked.Samples,
                ProcessorSeconds = Math.Round(Math.Max(0, SafeProcessorSeconds(tracked) - tracked.StartProcessorSeconds), 3),
                PeakManagedHeapBytes = tracked.PeakManagedHeap,
                AllocatedBytes = tracked.IsSelf ? GC.GetTotalAllocatedBytes() - tracked.StartAllocatedBytes : (long?)null,
                GcSeconds = tracked.IsSelf
                    ? Math.Round(GC.GetTotalPauseDuration().TotalSeconds - (tracked.StartGcSeconds ?? 0), 3)
                    : null,
            });
        }

        return summaries;
    }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            await SampleOnceAsync().ConfigureAwait(false);

            try
            {
                await Task.Delay(_interval, _stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return;
            }
        }
    }

    /// <summary>Takes one sample of every tracked process.</summary>
    /// <returns>A task that completes when the samples are written.</returns>
    public async Task SampleOnceAsync()
    {
        var offset = Stopwatch.GetElapsedTime(_windowStartTimestamp).TotalSeconds;
        var utc = DateTimeOffset.UtcNow;

        foreach (var tracked in _tracked)
        {
            long rss;

            try
            {
                tracked.Process.Refresh();
                rss = tracked.Process.WorkingSet64;
            }
            catch (InvalidOperationException)
            {
                // The process exited mid-window. Its series simply stops; a
                // zero sample would look like a process that freed its memory.
                continue;
            }

            var managedHeap = tracked.IsSelf ? GC.GetGCMemoryInfo().HeapSizeBytes : (long?)null;

            tracked.Samples++;
            tracked.RssTotal += rss;
            tracked.PeakRss = Math.Max(tracked.PeakRss, rss);

            if (managedHeap is { } heap)
            {
                tracked.PeakManagedHeap = Math.Max(tracked.PeakManagedHeap ?? 0, heap);
            }

            PeakRssBytes[tracked.Role] = tracked.PeakRss;

            await _writer.WriteAsync(new ResourceSample
            {
                CellId = _cellId,
                Role = tracked.Role,
                Pid = tracked.Pid,
                Utc = utc,
                OffsetSeconds = Math.Round(offset, 3),
                RssBytes = rss,
                ProcessorSeconds = Math.Round(SafeProcessorSeconds(tracked), 3),
                ManagedHeapBytes = managedHeap,
                AllocatedBytes = tracked.IsSelf ? GC.GetTotalAllocatedBytes() : (long?)null,
                GcSeconds = tracked.IsSelf ? Math.Round(GC.GetTotalPauseDuration().TotalSeconds, 3) : (double?)null,
            }).ConfigureAwait(false);
        }
    }

    private static double SafeProcessorSeconds(TrackedProcess tracked)
    {
        try
        {
            tracked.Process.Refresh();
            return tracked.Process.TotalProcessorTime.TotalSeconds;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // Processor time is not readable for this process on this platform.
            // The caller records that in the telemetry coverage; the value is
            // not invented.
            return tracked.StartProcessorSeconds;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _stop.Dispose();

        foreach (var tracked in _tracked)
        {
            tracked.Process.Dispose();
        }
    }

    private sealed record TrackedProcess(string Role, int Pid, Process Process, bool IsSelf)
    {
        public int Samples { get; set; }

        public long RssTotal { get; set; }

        public long PeakRss { get; set; }

        public long? PeakManagedHeap { get; set; }

        public double StartProcessorSeconds { get; set; }

        public long StartAllocatedBytes { get; set; }

        public double? StartGcSeconds { get; set; }
    }
}
