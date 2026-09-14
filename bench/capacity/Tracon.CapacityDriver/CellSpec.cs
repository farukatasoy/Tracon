using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracon.CapacityDriver;

/// <summary>What one measurement cell is: one scenario at one load point.</summary>
/// <remarks>
/// <para>
/// A cell is the unit `scripts/capacity.py` isolates: its own schema, its own
/// host process, its own repeat index. The driver runs exactly one and writes
/// exactly one <see cref="CellResult"/>; merging cells into a report is a
/// separate invocation over the files on disk, so the report and the raw
/// samples are always computed from the same data.
/// </para>
/// <para>
/// 🚨 There is no connection-string field. The driver reads the database from
/// the <c>TRACON_CAPACITY_CONNECTION</c> environment variable so that a
/// credential never reaches a process argument, a spec file, or the manifest.
/// </para>
/// </remarks>
public sealed class CellSpec
{
    /// <summary>The identifier of the whole capacity run this cell belongs to.</summary>
    public string RunId { get; set; } = "";

    /// <summary>The identifier of this cell, unique inside the run.</summary>
    public string CellId { get; set; } = "";

    /// <summary>The profile that produced this cell (<c>smoke</c>, <c>sweep</c>, <c>arrival</c>, <c>workers</c>, <c>soak</c>).</summary>
    public string Profile { get; set; } = "";

    /// <summary>The scenarios driven in this cell. More than one means an equal mix (the soak shape).</summary>
    public List<string> Scenarios { get; set; } = [];

    /// <summary>The seed shape the schema started from (<c>empty</c> or <c>full</c>).</summary>
    public string SeedShape { get; set; } = "empty";

    /// <summary>Which repeat of the same cell this is, starting at 1.</summary>
    public int Repeat { get; set; } = 1;

    /// <summary>Concurrent in-flight requests the closed-loop scenarios keep.</summary>
    public int Concurrency { get; set; } = 1;

    /// <summary>Planned requests per second. Non-null switches the driver to the open-loop arrival shape.</summary>
    public double? ArrivalRatePerSecond { get; set; }

    /// <summary>How many worker processes lease from the queue, or <see langword="null"/> when the axis is not measured.</summary>
    public int? WorkerCount { get; set; }

    /// <summary>The process ids of those worker processes.</summary>
    public List<int> WorkerPids { get; set; } = [];

    /// <summary>The HTTP host's process id, sampled separately from the driver's.</summary>
    public int HostPid { get; set; }

    /// <summary>Seconds of load thrown away before measurement starts.</summary>
    public double WarmupSeconds { get; set; }

    /// <summary>Seconds of measured load. Zero means the cell is bounded by <see cref="RunsPerTenant"/> instead.</summary>
    public double MeasureSeconds { get; set; }

    /// <summary>Requests per tenant when the cell is bounded by count rather than by duration.</summary>
    public int RunsPerTenant { get; set; }

    /// <summary>How long the driver waits for accepted work to finish after the window closes.</summary>
    public double DrainTimeoutSeconds { get; set; } = 300;

    /// <summary>How long a single request may take before the driver gives up on it.</summary>
    public double RequestTimeoutSeconds { get; set; } = 120;

    /// <summary>The host's Tracon prefix, e.g. <c>http://127.0.0.1:5199/tracon</c>.</summary>
    public string BaseAddress { get; set; } = "";

    /// <summary>The two synthetic tenants driven with an equal share.</summary>
    public List<string> Tenants { get; set; } = [];

    /// <summary>The catalog name of the agent under load.</summary>
    public string Agent { get; set; } = "capacity-agent";

    /// <summary>The database schema this cell owns end to end.</summary>
    public string Schema { get; set; } = "";

    /// <summary>Where the cell writes its own files.</summary>
    public string OutputDirectory { get; set; } = "";

    /// <summary>The synthetic workload the host's model provider replays.</summary>
    public WorkloadSpec Workload { get; set; } = new();

    /// <summary>The caps that stop the cell rather than let it exhaust the machine.</summary>
    public ResourceLimits Limits { get; set; } = new();

    /// <summary>How often the resource sampler takes a sample.</summary>
    public double ResourceSampleIntervalSeconds { get; set; } = 1;

    /// <summary>Whether run retention was on while the cell ran; storage numbers mean something else when it is.</summary>
    public bool RetentionEnabled { get; set; }

    /// <summary>The seed a randomized workload was drawn from, or <see langword="null"/> for the deterministic default.</summary>
    public int? WorkloadSeed { get; set; }

    /// <summary>Reads a spec from a JSON file.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The parsed spec.</returns>
    /// <exception cref="InvalidOperationException">The file does not hold a spec.</exception>
    public static CellSpec Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize(json, CapacityJson.Default.CellSpec)
            ?? throw new InvalidOperationException($"'{path}' does not contain a cell specification.");
    }

    /// <summary>Every reason this spec cannot be measured, in the order found.</summary>
    /// <returns>The problems; empty when the spec is usable.</returns>
    /// <remarks>
    /// 🚨 A zero or negative duration, a zero repeat count and a missing bound
    /// are rejected here rather than producing an empty report that looks like
    /// a fast one. A cell with no requests is not a fast cell.
    /// </remarks>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(RunId))
        {
            problems.Add("runId is required");
        }
        if (string.IsNullOrWhiteSpace(CellId))
        {
            problems.Add("cellId is required");
        }
        if (string.IsNullOrWhiteSpace(BaseAddress))
        {
            problems.Add("baseAddress is required");
        }
        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            problems.Add("outputDirectory is required");
        }
        if (string.IsNullOrWhiteSpace(Schema))
        {
            problems.Add("schema is required");
        }

        if (Scenarios.Count == 0)
        {
            problems.Add("at least one scenario is required");
        }

        foreach (var scenario in Scenarios)
        {
            if (!CapacityScenario.IsKnown(scenario))
            {
                problems.Add($"unknown scenario '{scenario}'");
            }
        }

        if (Tenants.Count != 2)
        {
            problems.Add("exactly two tenants are required; the reconciliation compares one against the other");
        }

        if (Concurrency <= 0)
        {
            problems.Add("concurrency must be greater than zero");
        }
        if (Repeat <= 0)
        {
            problems.Add("repeat must be greater than zero");
        }
        if (WarmupSeconds < 0)
        {
            problems.Add("warmupSeconds cannot be negative");
        }
        if (MeasureSeconds < 0)
        {
            problems.Add("measureSeconds cannot be negative");
        }
        if (RunsPerTenant < 0)
        {
            problems.Add("runsPerTenant cannot be negative");
        }

        if (MeasureSeconds <= 0 && RunsPerTenant <= 0)
        {
            problems.Add("a cell needs a bound: either measureSeconds or runsPerTenant must be greater than zero");
        }

        if (MeasureSeconds > 0 && RunsPerTenant > 0)
        {
            problems.Add("measureSeconds and runsPerTenant are two different bounds; give exactly one");
        }

        if (DrainTimeoutSeconds <= 0)
        {
            problems.Add("drainTimeoutSeconds must be greater than zero");
        }
        if (RequestTimeoutSeconds <= 0)
        {
            problems.Add("requestTimeoutSeconds must be greater than zero");
        }
        if (ResourceSampleIntervalSeconds <= 0)
        {
            problems.Add("resourceSampleIntervalSeconds must be greater than zero");
        }

        if (ArrivalRatePerSecond is { } rate && rate <= 0)
        {
            problems.Add("arrivalRatePerSecond must be greater than zero when present");
        }

        if (ArrivalRatePerSecond is not null && !Scenarios.Contains(CapacityScenario.Queued, StringComparer.Ordinal))
        {
            problems.Add("the arrival shape is only defined for the queued scenario");
        }

        if (WorkerCount is { } workers)
        {
            if (workers <= 0)
            {
                problems.Add("workerCount must be greater than zero when present");
            }
            else if (WorkerPids.Count != workers)
            {
                // 🚨 The whole worker axis shifts by one if the HTTP host's own
                // in-process worker is still leasing. capacity.py turns it off
                // and records the pids; this is the driver's own check that the
                // number of processes matches the label on the axis.
                problems.Add($"workerCount is {workers} but {WorkerPids.Count} worker pids were supplied");
            }
        }

        if (Concurrency > Limits.MaxInFlight)
        {
            problems.Add($"concurrency {Concurrency} exceeds the in-flight cap {Limits.MaxInFlight}");
        }

        problems.AddRange(Workload.Validate());
        problems.AddRange(Limits.Validate());

        return problems;
    }
}

/// <summary>The synthetic workload the host replays for every request.</summary>
public sealed class WorkloadSpec
{
    /// <summary>Bytes of UTF-8 in the request message.</summary>
    public int RequestBytes { get; set; } = 1024;

    /// <summary>Total milliseconds the model provider waits across all turns.</summary>
    public int ModelDelayMilliseconds { get; set; } = 1000;

    /// <summary>How many chunks the final answer is made of.</summary>
    public int OutputChunks { get; set; } = 20;

    /// <summary>ASCII characters per chunk.</summary>
    public int OutputChunkCharacters { get; set; } = 256;

    /// <summary>Bytes the code tool returns.</summary>
    public int ToolResultBytes { get; set; } = 1024;

    /// <summary>Whether the model calls the tool before it can answer.</summary>
    public bool ToolCall { get; set; } = true;

    /// <summary>Every reason this workload cannot be replayed.</summary>
    /// <returns>The problems; empty when the workload is usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (RequestBytes <= 0)
        {
            problems.Add("workload.requestBytes must be greater than zero");
        }
        if (ModelDelayMilliseconds < 0)
        {
            problems.Add("workload.modelDelayMilliseconds cannot be negative");
        }
        if (OutputChunks <= 0)
        {
            problems.Add("workload.outputChunks must be greater than zero");
        }
        if (OutputChunkCharacters <= 0)
        {
            problems.Add("workload.outputChunkCharacters must be greater than zero");
        }
        if (ToolResultBytes < 0)
        {
            problems.Add("workload.toolResultBytes cannot be negative");
        }

        return problems;
    }
}

/// <summary>The caps that stop a cell instead of letting it exhaust the machine.</summary>
/// <remarks>
/// 🚨 None of these is a success threshold. Hitting one produces an
/// <c>incomplete/resource-limit</c> cell, which is a finding about the
/// measurement's reach, not a failure of the software under measurement.
/// </remarks>
public sealed class ResourceLimits
{
    /// <summary>The most requests the driver keeps in flight at once.</summary>
    public int MaxInFlight { get; set; } = 128;

    /// <summary>The host's resident set ceiling.</summary>
    public long HostRssBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    /// <summary>The driver's own resident set ceiling.</summary>
    public long DriverRssBytes { get; set; } = 1024L * 1024 * 1024;

    /// <summary>The schema's total relation size ceiling.</summary>
    public long DatabaseBytes { get; set; } = 5L * 1024 * 1024 * 1024;

    /// <summary>The free disk the run refuses to drop below.</summary>
    public long MinimumFreeDiskBytes { get; set; } = 10L * 1024 * 1024 * 1024;

    /// <summary>The most raw request rows one cell writes before the writer starts counting instead of appending.</summary>
    public int MaxRequestSamples { get; set; } = 200_000;

    /// <summary>Every reason these limits cannot bound a cell.</summary>
    /// <returns>The problems; empty when the limits are usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (MaxInFlight <= 0)
        {
            problems.Add("limits.maxInFlight must be greater than zero");
        }
        if (HostRssBytes <= 0)
        {
            problems.Add("limits.hostRssBytes must be greater than zero");
        }
        if (DriverRssBytes <= 0)
        {
            problems.Add("limits.driverRssBytes must be greater than zero");
        }
        if (DatabaseBytes <= 0)
        {
            problems.Add("limits.databaseBytes must be greater than zero");
        }
        if (MinimumFreeDiskBytes < 0)
        {
            problems.Add("limits.minimumFreeDiskBytes cannot be negative");
        }
        if (MaxRequestSamples <= 0)
        {
            problems.Add("limits.maxRequestSamples must be greater than zero");
        }

        return problems;
    }
}

/// <summary>The three HTTP shapes under measurement.</summary>
public static class CapacityScenario
{
    /// <summary>A buffered POST carrying <c>Idempotency-Key</c>.</summary>
    public const string Buffered = "buffered";

    /// <summary>The same POST without the header, consumed as live SSE.</summary>
    public const string Streaming = "streaming";

    /// <summary>The same POST with <c>Prefer: respond-async</c>, then the recorded event stream.</summary>
    public const string Queued = "queued";

    /// <summary>Whether a name is one of the three.</summary>
    /// <param name="name">The candidate.</param>
    /// <returns><see langword="true"/> when the name is measurable.</returns>
    public static bool IsKnown(string? name)
        => name is Buffered or Streaming or Queued;
}

/// <summary>The serializer context the whole apparatus shares.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CellSpec))]
[JsonSerializable(typeof(CellResult))]
[JsonSerializable(typeof(RunSummary))]
[JsonSerializable(typeof(RequestSample))]
[JsonSerializable(typeof(ResourceSample))]
[JsonSerializable(typeof(RunManifest))]
public sealed partial class CapacityJson : JsonSerializerContext;
