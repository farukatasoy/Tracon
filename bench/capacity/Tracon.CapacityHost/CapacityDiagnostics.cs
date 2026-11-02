using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace Tracon.CapacityHost;

/// <summary>What the host process knows about its own footprint, taken in one call.</summary>
/// <remarks>
/// <para>
/// 🚨 Everything here is read from the running process, for the one question a
/// soak asks: does anything grow with time, or with the number of tenants? Each
/// field is a different way of answering it. A working set that grows while the
/// managed heap stays flat is native memory or fragmentation; a thread or timer
/// count that grows is something that was started and never stopped; a file
/// descriptor count that grows is a connection or file that was never closed;
/// a series count that grows is a metric tag that follows a caller-chosen value.
/// </para>
/// <para>
/// A reading that cannot be taken on this platform is <see langword="null"/>
/// and the caller records it as unavailable. It is never written as zero.
/// </para>
/// </remarks>
public static class CapacityDiagnostics
{
    /// <summary>Takes one snapshot.</summary>
    /// <param name="meters">The series counter, or <see langword="null"/> when it is not running.</param>
    /// <param name="collect">
    /// Whether to run a full blocking, compacting collection first and report the heap that survives it.
    /// 🚨 That pauses the process; the driver asks for it every thirty seconds, not every second.
    /// </param>
    /// <param name="observe">Whether to ask every observable instrument to publish first.</param>
    /// <returns>The snapshot, ready to serialize.</returns>
    public static Dictionary<string, object?> Snapshot(MeterSeriesCounter? meters, bool collect, bool observe)
    {
        long? liveHeap = null;

        if (collect)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            liveHeap = GC.GetTotalMemory(forceFullCollection: false);
        }

        if (observe)
        {
            meters?.Observe();
        }

        var info = GC.GetGCMemoryInfo();
        using var process = Process.GetCurrentProcess();

        // Where the platform has no such figure the property reads zero; a zero would pass for a measurement.
        var handleCount = Positive(SafeLong(() => process.HandleCount));
        var privateBytes = Positive(SafeLong(() => process.PrivateMemorySize64));

        var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["processId"] = Environment.ProcessId,
            ["collected"] = collect,
            ["liveHeapBytes"] = liveHeap,
            ["gcTotalMemoryBytes"] = GC.GetTotalMemory(forceFullCollection: false),
            ["gcHeapSizeBytes"] = info.HeapSizeBytes,
            ["gcCommittedBytes"] = info.TotalCommittedBytes,
            ["gcFragmentedBytes"] = info.FragmentedBytes,
            ["gcAllocatedBytes"] = GC.GetTotalAllocatedBytes(),
            ["gcPauseSeconds"] = GC.GetTotalPauseDuration().TotalSeconds,
            ["gcGen0"] = GC.CollectionCount(0),
            ["gcGen1"] = GC.CollectionCount(1),
            ["gcGen2"] = GC.CollectionCount(2),
            ["threadPoolThreads"] = ThreadPool.ThreadCount,
            ["threadPoolPending"] = ThreadPool.PendingWorkItemCount,
            ["threadPoolCompleted"] = ThreadPool.CompletedWorkItemCount,
            ["activeTimers"] = Timer.ActiveCount,
            ["lockContentions"] = Monitor.LockContentionCount,
            ["loadedAssemblies"] = AppDomain.CurrentDomain.GetAssemblies().Length,
            ["workingSetBytes"] = SafeLong(() => process.WorkingSet64),
            ["privateBytes"] = privateBytes,
            ["osThreads"] = SafeLong(() => process.Threads.Count),
            ["handleCount"] = handleCount,
            ["openDescriptors"] = OpenDescriptors(),
            ["ledgerInFlight"] = CapacityRunLedger.InFlight,
            ["meterInstruments"] = meters?.Instruments,
            ["meterSeries"] = meters?.SeriesTotal,
            ["meterSaturated"] = meters?.Saturated,
            ["meterPublications"] = meters?.Publications,
            ["meterRepeatedPublications"] = meters?.RepeatedPublications(),
            ["meterSeriesByInstrument"] = meters?.SeriesByInstrument(),
            ["upDownTotals"] = meters?.UpDownTotals(),
        };

        return snapshot;
    }

    /// <summary>Counts the file descriptors this process holds open.</summary>
    /// <returns>The count, or <see langword="null"/> when the platform offers no cheap way to read it.</returns>
    /// <remarks>
    /// The directory listing opens one descriptor of its own, so the count is one higher than the number of
    /// descriptors the process held a moment before. The offset is constant, which is all a slope needs.
    /// </remarks>
    public static long? OpenDescriptors()
    {
        string? directory = null;

        if (OperatingSystem.IsLinux())
        {
            directory = "/proc/self/fd";
        }
        else if (OperatingSystem.IsMacOS())
        {
            directory = "/dev/fd";
        }

        if (directory is null)
        {
            return null;
        }

        try
        {
            return Directory.GetFileSystemEntries(directory).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>A short description of the runtime the snapshot came from, for the manifest.</summary>
    /// <returns>Runtime, GC mode and platform.</returns>
    public static Dictionary<string, object?> Describe()
        => new(StringComparer.Ordinal)
        {
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["os"] = RuntimeInformation.OSDescription,
            ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["serverGc"] = GCSettings.IsServerGC,
            ["gcLatencyMode"] = GCSettings.LatencyMode.ToString(),
            ["processorCount"] = Environment.ProcessorCount,
        };

    private static long? Positive(long? value) => value is > 0 ? value : null;

    private static long? SafeLong(Func<long> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or PlatformNotSupportedException
            or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
