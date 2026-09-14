namespace Tracon.CapacityDriver;

/// <summary>Everything one measured cell produced.</summary>
public sealed class CellResult
{
    /// <summary>The capacity run this cell belongs to.</summary>
    public string RunId { get; set; } = "";

    /// <summary>This cell's identifier inside the run.</summary>
    public string CellId { get; set; } = "";

    /// <summary>The profile that produced the cell.</summary>
    public string Profile { get; set; } = "";

    /// <summary>The scenarios driven.</summary>
    public List<string> Scenarios { get; set; } = [];

    /// <summary>The seed shape the schema started from.</summary>
    public string SeedShape { get; set; } = "empty";

    /// <summary>The repeat index.</summary>
    public int Repeat { get; set; }

    /// <summary>The in-flight concurrency the closed-loop scenarios kept.</summary>
    public int Concurrency { get; set; }

    /// <summary>Planned requests per second, when the cell used the open-loop shape.</summary>
    public double? ArrivalRatePerSecond { get; set; }

    /// <summary>The number of worker processes, when the worker axis was measured.</summary>
    public int? WorkerCount { get; set; }

    /// <summary><c>complete</c>, <c>incomplete/...</c> or <c>invalid/...</c>.</summary>
    /// <remarks>
    /// 🚨 Slowness never produces <c>invalid</c>. Invalid means the measurement
    /// itself cannot be trusted: a counting error, lost data, tenant bleed, a
    /// missing mandatory measurement, or two workers running one job at once.
    /// </remarks>
    public string Status { get; set; } = CellStatus.Complete;

    /// <summary>Why the cell is not <c>complete</c>, in the reader's language.</summary>
    public List<string> StatusReasons { get; set; } = [];

    /// <summary>Findings that do not invalidate the cell but change how it must be read.</summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>When the measured window opened, for correlation only.</summary>
    public DateTimeOffset WindowStartUtc { get; set; }

    /// <summary>How long the measured window actually lasted.</summary>
    public double MeasuredSeconds { get; set; }

    /// <summary>How long the drain phase took after the window closed.</summary>
    public double DrainSeconds { get; set; }

    /// <summary>What became of every planned request.</summary>
    public ArrivalTally Arrival { get; set; } = new();

    /// <summary>Throughput over the measured window, in completed requests per second.</summary>
    /// <remarks>The drain phase is excluded from this denominator on purpose.</remarks>
    public double ThroughputPerSecond { get; set; }

    /// <summary>End-to-end latency of the requests that succeeded inside the window, by scenario.</summary>
    public Dictionary<string, LatencySummary> LatencyByScenario { get; set; } = [];

    /// <summary>Time to the first piece of content, by scenario.</summary>
    public Dictionary<string, LatencySummary> FirstContentByScenario { get; set; } = [];

    /// <summary>How long a queued job waited before its first attempt started.</summary>
    public LatencySummary QueueWait { get; set; } = new();

    /// <summary>What the model provider actually spent, as the host reported it.</summary>
    public ModelSummary Model { get; set; } = new();

    /// <summary>Process resources, one entry per sampled process.</summary>
    public List<ProcessResourceSummary> Processes { get; set; } = [];

    /// <summary>What the schema grew by, per table.</summary>
    public StorageSummary Storage { get; set; } = new();

    /// <summary>How the queued work spread across worker processes.</summary>
    public WorkerSummary? Workers { get; set; }

    /// <summary>The reconciliation between what was sent and what the store holds.</summary>
    public ReconciliationSummary Reconciliation { get; set; } = new();

    /// <summary>Which measurements were available on this platform, and why the others were not.</summary>
    public TelemetryCoverage Telemetry { get; set; } = new();

    /// <summary>Marks the cell invalid with a reason. Repeated calls accumulate reasons.</summary>
    /// <param name="reason">Why the measurement cannot be trusted.</param>
    public void Invalidate(string reason)
    {
        Status = CellStatus.Invalid;
        StatusReasons.Add(reason);
    }

    /// <summary>Marks the cell incomplete with a reason, unless it is already invalid.</summary>
    /// <param name="reason">Why the cell stopped short.</param>
    public void Incomplete(string reason)
    {
        if (!string.Equals(Status, CellStatus.Invalid, StringComparison.Ordinal))
        {
            Status = CellStatus.IncompletePrefix + reason;
        }

        StatusReasons.Add(reason);
    }
}

/// <summary>The three validity states a cell can end in.</summary>
public static class CellStatus
{
    /// <summary>The cell ran its whole window and every mandatory measurement is present.</summary>
    public const string Complete = "complete";

    /// <summary>The cell stopped short. The prefix is followed by the reason.</summary>
    public const string IncompletePrefix = "incomplete/";

    /// <summary>The measurement cannot be trusted. Never used for slowness.</summary>
    public const string Invalid = "invalid";

    /// <summary>A cell stopped by a resource cap.</summary>
    public const string ResourceLimit = "resource-limit";

    /// <summary>A cell whose accepted work did not finish inside the drain budget.</summary>
    public const string DrainLimit = "drain-limit";

    /// <summary>A cell interrupted by the operator.</summary>
    public const string Interrupted = "interrupted";
}

/// <summary>What the model boundary actually cost, as the host measured it.</summary>
/// <remarks>
/// 🚨 This is never subtracted from the client's own percentile. The two are
/// measured on different clocks in different processes; the report shows both
/// and draws no arithmetic between them.
/// </remarks>
public sealed class ModelSummary
{
    /// <summary>How many model calls the host made.</summary>
    public long Calls { get; set; }

    /// <summary>How many turns those calls took.</summary>
    public long Turns { get; set; }

    /// <summary>What the provider boundary actually spent, on the host's clock.</summary>
    public LatencySummary Duration { get; set; } = new();

    /// <summary>How many times the code tool ran.</summary>
    public long ToolInvocations { get; set; }
}

/// <summary>One process's resource time series, reduced.</summary>
public sealed class ProcessResourceSummary
{
    /// <summary>What the process is: <c>host</c>, <c>driver</c> or <c>worker-N</c>.</summary>
    public string Role { get; set; } = "";

    /// <summary>The operating-system process id.</summary>
    public int Pid { get; set; }

    /// <summary>How many samples were taken.</summary>
    public int Samples { get; set; }

    /// <summary>Peak resident set, in bytes.</summary>
    public long PeakRssBytes { get; set; }

    /// <summary>Mean resident set, in bytes.</summary>
    public long MeanRssBytes { get; set; }

    /// <summary>Total processor time consumed during the window, in seconds.</summary>
    public double ProcessorSeconds { get; set; }

    /// <summary>Peak managed heap, in bytes, when the process reported one.</summary>
    public long? PeakManagedHeapBytes { get; set; }

    /// <summary>Total bytes allocated during the window, when the process reported them.</summary>
    public long? AllocatedBytes { get; set; }

    /// <summary>Total time spent in garbage collection, when the process reported it.</summary>
    public double? GcSeconds { get; set; }
}

/// <summary>What one cell's schema grew by, table by table.</summary>
/// <remarks>
/// 🚨 Byte growth is taken from <c>pg_total_relation_size</c>, which counts
/// bloat as well as data. The three defences are a fresh schema per cell, the
/// autovacuum state recorded on both sides of the window, and taking the
/// reading only after the drain has finished. When autovacuum ran inside the
/// window the cell is flagged and its bytes are kept out of any average -
/// the row counts stay usable, which is exactly why rows and bytes are always
/// reported together.
/// </remarks>
public sealed class StorageSummary
{
    /// <summary>Whether the numbers below could be taken at all.</summary>
    public bool Available { get; set; }

    /// <summary>Why they could not, when they could not.</summary>
    public string? Unavailable { get; set; }

    /// <summary>Whether autovacuum ran on a measured table inside the window.</summary>
    public bool VacuumInterference { get; set; }

    /// <summary>Whether retention was deleting rows while the cell ran.</summary>
    public bool RetentionEnabled { get; set; }

    /// <summary>The completed runs the growth below is divided by.</summary>
    public long CompletedRuns { get; set; }

    /// <summary>The recorded events the growth below is divided by.</summary>
    public long RecordedEvents { get; set; }

    /// <summary>Per-table growth over the window.</summary>
    public List<TableGrowth> Tables { get; set; } = [];

    /// <summary>Total bytes written per completed run, across every measured table.</summary>
    public double? BytesPerRun { get; set; }

    /// <summary>Total bytes written per recorded event.</summary>
    public double? BytesPerEvent { get; set; }

    /// <summary>Total rows written per completed run.</summary>
    public double? RowsPerRun { get; set; }
}

/// <summary>How one table grew across the measured window.</summary>
public sealed class TableGrowth
{
    /// <summary>The table's name inside the cell's schema.</summary>
    public string Table { get; set; } = "";

    /// <summary>Rows added.</summary>
    public long Rows { get; set; }

    /// <summary>Bytes added to the table's own heap, TOAST excluded.</summary>
    public long HeapBytes { get; set; }

    /// <summary>Bytes added to the table's indexes.</summary>
    public long IndexBytes { get; set; }

    /// <summary>Bytes added to the table's TOAST storage.</summary>
    public long ToastBytes { get; set; }

    /// <summary>Bytes added in total.</summary>
    public long TotalBytes { get; set; }

    /// <summary>Dead tuples the table held when the window closed.</summary>
    public long DeadTuples { get; set; }

    /// <summary>Whether autovacuum touched this table inside the window.</summary>
    public bool Vacuumed { get; set; }
}

/// <summary>How the queued work spread across worker processes.</summary>
public sealed class WorkerSummary
{
    /// <summary>The host's effective <c>Scheduling:RunWorker</c> value, read at run time.</summary>
    /// <remarks>
    /// 🚨 <see langword="true"/> means the axis is wrong: the "1 worker" cell
    /// would be two workers and every point on the axis would shift. The cell
    /// refuses to run rather than produce a shifted series.
    /// </remarks>
    public bool HostRunWorker { get; set; }

    /// <summary>How many worker processes leased from the queue.</summary>
    public int WorkerCount { get; set; }

    /// <summary>What each of them did.</summary>
    public List<WorkerShare> Shares { get; set; } = [];

    /// <summary>Jobs accepted into the queue during the window.</summary>
    public long AcceptedJobs { get; set; }

    /// <summary>
    /// Jobs that actually ran somewhere. Below <see cref="AcceptedJobs"/> means
    /// the queue was still holding work; that gap is a finding, not a rounding.
    /// </summary>
    public long ExecutedJobs { get; set; }

    /// <summary>Attempts recorded across those jobs.</summary>
    public long Attempts { get; set; }

    /// <summary>Attempts divided by accepted jobs. Above one is an at-least-once finding, not a failure.</summary>
    public double? AttemptRatio { get; set; }

    /// <summary>Lease renewals observed.</summary>
    public long LeaseRenewals { get; set; }

    /// <summary>Take-overs observed: a job leased by a second worker after the first lost its lease.</summary>
    public long TakeOvers { get; set; }

    /// <summary>The share of jobs the busiest worker ran, between 0 and 1.</summary>
    public double? LargestShare { get; set; }

    /// <summary>Whether contention could be measured at all, or one worker simply took everything.</summary>
    public bool ContentionMeasured { get; set; }

    /// <summary>Pairs of attempts on the same job whose execution intervals overlapped.</summary>
    /// <remarks>
    /// At-least-once allows an extra attempt (K-641). It does not allow an
    /// overlapping one: a non-zero value here makes the cell <c>invalid</c>.
    /// </remarks>
    public long ConcurrentOverlaps { get; set; }
}

/// <summary>What one worker process did.</summary>
public sealed class WorkerShare
{
    /// <summary>The process id, so the reader can tie a share to a resource series.</summary>
    public int Pid { get; set; }

    /// <summary>The label the worker wrote into its own claims.</summary>
    public string Name { get; set; } = "";

    /// <summary>How many jobs it ran.</summary>
    public long Jobs { get; set; }

    /// <summary>How many attempts it made.</summary>
    public long Attempts { get; set; }
}

/// <summary>Whether what was sent and what the store holds agree.</summary>
public sealed class ReconciliationSummary
{
    /// <summary>Distinct run ids the server accepted.</summary>
    public long AcceptedRunIds { get; set; }

    /// <summary>How many of those reached a terminal status.</summary>
    public long TerminalRuns { get; set; }

    /// <summary>Accepted run ids the store does not hold.</summary>
    public long MissingRuns { get; set; }

    /// <summary>Answers whose correlation or checksum did not match.</summary>
    public long ContentMismatches { get; set; }

    /// <summary>Recorded event streams whose sequence was not contiguous.</summary>
    public long SequenceGaps { get; set; }

    /// <summary>Requests whose event subscriber attached while the run was still live.</summary>
    public long LiveSubscriptions { get; set; }

    /// <summary>Runs whose events were only read after the run had finished.</summary>
    public long HistoricalOnlySubscriptions { get; set; }

    /// <summary>Rows one tenant could see that belong to the other. Any value above zero invalidates the cell.</summary>
    public long TenantBleed { get; set; }

    /// <summary>Cross-tenant read attempts the server correctly refused.</summary>
    public long CrossTenantRefusals { get; set; }

    /// <summary>Whether every mandatory reconciliation could be performed.</summary>
    public bool Complete { get; set; }
}

/// <summary>Which measurements this platform could actually supply.</summary>
/// <remarks>
/// 🚨 A measurement that could not be taken is never written as zero. It is
/// listed here with the reason it was unavailable, and the report says so.
/// </remarks>
public sealed class TelemetryCoverage
{
    /// <summary>The measurements that were taken.</summary>
    public List<string> Available { get; set; } = [];

    /// <summary>The measurements that were not, each with its reason.</summary>
    public Dictionary<string, string> Unavailable { get; set; } = [];

    /// <summary>The mandatory measurements that were missing. A non-empty list invalidates the cell.</summary>
    public List<string> MissingMandatory { get; set; } = [];
}
