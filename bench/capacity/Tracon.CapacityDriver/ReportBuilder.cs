using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Tracon.CapacityDriver;

/// <summary>The whole run, reduced from its cells.</summary>
public sealed class RunSummary
{
    /// <summary>The run's identifier.</summary>
    public string RunId { get; set; } = "";

    /// <summary>The profile that produced it.</summary>
    public string Profile { get; set; } = "";

    /// <summary>How many cells were attempted.</summary>
    public int Cells { get; set; }

    /// <summary>How many finished their whole window with every mandatory measurement present.</summary>
    public int CompleteCells { get; set; }

    /// <summary>How many stopped short.</summary>
    public int IncompleteCells { get; set; }

    /// <summary>How many produced a measurement that cannot be trusted.</summary>
    public int InvalidCells { get; set; }

    /// <summary>One row per load point, with repeats merged at the sample level.</summary>
    public List<SummaryRow> Rows { get; set; } = [];

    /// <summary>One entry per cell: the counters and the reconciliation the page's claims rest on.</summary>
    /// <remarks>
    /// 🚨 These used to live only in the untracked per-cell files, so a number
    /// published from them could not be checked against anything kept. A shipped
    /// claim whose evidence exists on one machine is not evidence.
    /// </remarks>
    public List<CellEvidence> Evidence { get; set; } = [];

    /// <summary>What the first bottleneck was, or why it could not be determined.</summary>
    public string Bottleneck { get; set; } = "not determined";

    /// <summary>Everything the reader must know before reading a number above.</summary>
    public List<string> Caveats { get; set; } = [];
}

/// <summary>What one cell counted, kept so a published number can be checked.</summary>
public sealed class CellEvidence
{
    /// <summary>The cell.</summary>
    public string CellId { get; set; } = "";

    /// <summary><c>complete</c>, <c>incomplete/...</c> or <c>invalid</c>.</summary>
    public string Status { get; set; } = "";

    /// <summary>Planned requests per second, when the cell used the open-loop shape.</summary>
    public double? ArrivalRatePerSecond { get; set; }

    /// <summary>How many worker processes leased, for the worker axis.</summary>
    public int? WorkerCount { get; set; }

    /// <summary>How long the measured window lasted.</summary>
    public double MeasuredSeconds { get; set; }

    /// <summary>How long the drain took after it closed.</summary>
    public double DrainSeconds { get; set; }

    /// <summary>What became of every planned request.</summary>
    public ArrivalTally Arrival { get; set; } = new();

    /// <summary>Whether what was sent and what the store holds agree.</summary>
    public ReconciliationSummary Reconciliation { get; set; } = new();

    /// <summary>How long a queued job waited before its first attempt.</summary>
    public LatencySummary QueueWait { get; set; } = new();

    /// <summary>Per-process resource use, one entry per sampled process.</summary>
    public List<ProcessResourceSummary> Processes { get; set; } = [];

    /// <summary>How the queued work spread across worker processes.</summary>
    public WorkerSummary? Workers { get; set; }

    /// <summary>The host's effective in-process worker setting, read at run time.</summary>
    public bool? HostRunWorker { get; set; }

    /// <summary>Which measurements this platform supplied, and which it did not.</summary>
    public TelemetryCoverage Telemetry { get; set; } = new();

    /// <summary>Findings that change how the cell must be read.</summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>Why the cell is not complete.</summary>
    public List<string> StatusReasons { get; set; } = [];
}

/// <summary>One load point, with its repeats merged.</summary>
public sealed class SummaryRow
{
    /// <summary>The scenario.</summary>
    public string Scenario { get; set; } = "";

    /// <summary>The seed shape the schema started from.</summary>
    public string SeedShape { get; set; } = "";

    /// <summary>In-flight concurrency.</summary>
    public int Concurrency { get; set; }

    /// <summary>Planned requests per second, for the open-loop shape.</summary>
    public double? ArrivalRatePerSecond { get; set; }

    /// <summary>How many worker processes leased, for the worker axis.</summary>
    public int? WorkerCount { get; set; }

    /// <summary>How many repeats were merged into this row.</summary>
    public int Repeats { get; set; }

    /// <summary>
    /// How many of those repeats contributed a BYTE growth number.
    /// </summary>
    /// <remarks>
    /// 🚨 Fewer than <see cref="Repeats"/> means autovacuum disturbed the rest.
    /// One is not an average, and publishing it as though it were is exactly
    /// what the phase's own rule forbids.
    /// </remarks>
    public int StorageRepeats { get; set; }

    /// <summary>How many of those repeats completed.</summary>
    public int CompleteRepeats { get; set; }

    /// <summary>End-to-end latency, computed once over every repeat's samples.</summary>
    public LatencySummary Latency { get; set; } = new();

    /// <summary>Time to first content, computed the same way.</summary>
    public LatencySummary FirstContent { get; set; } = new();

    /// <summary>Mean throughput across the repeats, in completed requests per second.</summary>
    public double ThroughputPerSecond { get; set; }

    /// <summary>The spread of throughput across the repeats, as max minus min.</summary>
    public double ThroughputSpread { get; set; }

    /// <summary>Bytes written per completed run, when the cells could account for storage.</summary>
    public double? BytesPerRun { get; set; }

    /// <summary>Rows written per completed run.</summary>
    public double? RowsPerRun { get; set; }

    /// <summary>Bytes written per recorded event.</summary>
    public double? BytesPerEvent { get; set; }

    /// <summary>Whether any repeat's byte growth was disturbed by autovacuum.</summary>
    public bool VacuumInterference { get; set; }

    /// <summary>The largest share of jobs one worker took, for the worker axis.</summary>
    public double? LargestWorkerShare { get; set; }

    /// <summary>Attempts divided by accepted jobs, for the worker axis.</summary>
    public double? AttemptRatio { get; set; }

    /// <summary>Whether contention could actually be measured at this point.</summary>
    public bool? ContentionMeasured { get; set; }
}

/// <summary>Merges the cells of a run into one summary, one report and its charts.</summary>
/// <remarks>
/// 🚨 The report is computed from the cell files on disk, never from state the
/// measuring process happened to hold. That is what makes the Markdown, the
/// JSON and the charts reproducible from the same raw data - and what lets an
/// interrupted run still produce a partial report from the cells it did finish.
/// </remarks>
public static class ReportBuilder
{
    /// <summary>Builds every artifact of a finished or partial run.</summary>
    /// <param name="runDirectory">The run's artifact directory.</param>
    /// <returns>The summary that was written.</returns>
    public static RunSummary Build(string runDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);

        var cells = LoadCells(runDirectory);
        var manifest = LoadManifest(runDirectory);

        var summary = new RunSummary
        {
            RunId = manifest?.RunId ?? Path.GetFileName(runDirectory.TrimEnd(Path.DirectorySeparatorChar)),
            Profile = manifest?.Profile ?? cells.FirstOrDefault()?.Profile ?? "",
            Cells = cells.Count,
            CompleteCells = cells.Count(static c => string.Equals(c.Status, CellStatus.Complete, StringComparison.Ordinal)),
            InvalidCells = cells.Count(static c => string.Equals(c.Status, CellStatus.Invalid, StringComparison.Ordinal)),
        };

        summary.IncompleteCells = summary.Cells - summary.CompleteCells - summary.InvalidCells;
        summary.Rows = BuildRows(cells, runDirectory);
        summary.Evidence = cells.ConvertAll(static cell => new CellEvidence
        {
            CellId = cell.CellId,
            Status = cell.Status,
            ArrivalRatePerSecond = cell.ArrivalRatePerSecond,
            WorkerCount = cell.WorkerCount,
            MeasuredSeconds = cell.MeasuredSeconds,
            DrainSeconds = cell.DrainSeconds,
            Arrival = cell.Arrival,
            Reconciliation = cell.Reconciliation,
            QueueWait = cell.QueueWait,
            Processes = cell.Processes,
            Workers = cell.Workers,
            HostRunWorker = cell.Workers?.HostRunWorker,
            Telemetry = cell.Telemetry,
            Warnings = cell.Warnings,
            StatusReasons = cell.StatusReasons,
        });

        summary.Bottleneck = DescribeBottleneck(cells, summary.Rows);
        summary.Caveats = BuildCaveats(cells, manifest);

        File.WriteAllText(
            Path.Combine(runDirectory, "summary.json"),
            Redactor.Scrub(JsonSerializer.Serialize(summary, CapacityJson.Default.RunSummary)));

        File.WriteAllText(
            Path.Combine(runDirectory, "report.md"),
            Redactor.Scrub(RenderMarkdown(summary, manifest, cells)));

        WriteCharts(runDirectory, summary);
        return summary;
    }

    /// <summary>Reads every cell result a run directory holds, in cell-id order.</summary>
    /// <param name="runDirectory">The run's artifact directory.</param>
    /// <returns>The cells.</returns>
    public static List<CellResult> LoadCells(string runDirectory)
    {
        var cells = new List<CellResult>();

        foreach (var file in Directory.EnumerateFiles(runDirectory, "cell.json", SearchOption.AllDirectories))
        {
            if (JsonSerializer.Deserialize(File.ReadAllText(file), CapacityJson.Default.CellResult) is { } cell)
            {
                cells.Add(cell);
            }
        }

        cells.Sort(static (left, right) => string.CompareOrdinal(left.CellId, right.CellId));
        return cells;
    }

    /// <summary>Reads one cell's measured latency samples, by scenario.</summary>
    /// <param name="runDirectory">The run's artifact directory.</param>
    /// <param name="cellId">The cell to read.</param>
    /// <returns>End-to-end and first-content samples per scenario, or empty when the raw file is gone.</returns>
    /// <remarks>
    /// 🚨 Repeats must merge at the SAMPLE level. Averaging three repeats'
    /// p95 values - even weighted by their counts - is an arithmetic operation
    /// with no distributional meaning: three repeats at 1000, 1000 and 5000 ms
    /// publish 2333 ms, a number no request ever saw. It also hides the low
    /// sample warning, because each repeat can sit below the floor while their
    /// sum does not. So the raw file is read back here rather than the reduced
    /// summary in cell.json.
    /// </remarks>
    public static (Dictionary<string, List<double>> Total, Dictionary<string, List<double>> FirstContent) LoadSamples(
        string runDirectory,
        string cellId)
    {
        var total = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        var firstContent = new Dictionary<string, List<double>>(StringComparer.Ordinal);

        var path = Directory
            .EnumerateFiles(runDirectory, "requests.jsonl", SearchOption.AllDirectories)
            .FirstOrDefault(candidate => string.Equals(
                Path.GetFileName(Path.GetDirectoryName(candidate)), cellId, StringComparison.Ordinal));

        if (path is null)
        {
            return (total, firstContent);
        }

        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0)
            {
                continue;
            }

            RequestSample? sample;

            try
            {
                sample = JsonSerializer.Deserialize(line, CapacityJson.Default.RequestSample);
            }
            catch (JsonException)
            {
                // A torn final line (an interrupted run) loses one sample, not
                // the file. The count difference shows in the reconciliation.
                continue;
            }

            // Warm-up never enters a statistic, and only a clean success has a
            // latency worth a percentile.
            if (sample is null
                || sample.Warmup
                || !string.Equals(sample.Outcome, RequestOutcome.Accepted, StringComparison.Ordinal))
            {
                continue;
            }

            if (sample.TotalMilliseconds is { } elapsed)
            {
                Add(total, sample.Scenario, elapsed);
            }

            if (sample.FirstContentMilliseconds is { } first)
            {
                Add(firstContent, sample.Scenario, first);
            }
        }

        return (total, firstContent);

        static void Add(Dictionary<string, List<double>> into, string scenario, double value)
        {
            if (!into.TryGetValue(scenario, out var values))
            {
                values = [];
                into[scenario] = values;
            }

            values.Add(value);
        }
    }

    private static RunManifest? LoadManifest(string runDirectory)
    {
        var path = Path.Combine(runDirectory, "manifest.json");

        return File.Exists(path)
            ? JsonSerializer.Deserialize(File.ReadAllText(path), CapacityJson.Default.RunManifest)
            : null;
    }

    private static List<SummaryRow> BuildRows(List<CellResult> cells, string runDirectory)
    {
        var rows = new List<SummaryRow>();

        var groups = cells
            .Where(static c => !string.Equals(c.Status, CellStatus.Invalid, StringComparison.Ordinal))
            .SelectMany(static c => c.Scenarios.Select(scenario => (Cell: c, Scenario: scenario)))
            .GroupBy(static pair => (pair.Scenario, pair.Cell.SeedShape, pair.Cell.Concurrency, pair.Cell.ArrivalRatePerSecond, pair.Cell.WorkerCount));

        foreach (var group in groups)
        {
            var members = group.ToList();

            // 🚨 The repeats' SAMPLES are merged and the percentile is computed
            // once. Reducing each repeat first and averaging the reductions
            // publishes a number no request ever saw.
            var totalSamples = new List<double>();
            var firstSamples = new List<double>();
            var perRepeatCounts = new List<int>();

            foreach (var member in members)
            {
                var (total, first) = LoadSamples(runDirectory, member.Cell.CellId);
                var forScenario = total.GetValueOrDefault(member.Scenario) ?? [];

                totalSamples.AddRange(forScenario);
                firstSamples.AddRange(first.GetValueOrDefault(member.Scenario) ?? []);
                perRepeatCounts.Add(forScenario.Count);
            }

            // Only when the raw samples are gone does the reduced summary stand
            // in - and the row says so rather than pretending otherwise.
            var rawAvailable = totalSamples.Count > 0;

            var latency = rawAvailable
                ? LatencyStatistics.From(totalSamples).ToSummary()
                : MergeSummaries(members.ConvertAll(m => m.Cell.LatencyByScenario.GetValueOrDefault(m.Scenario) ?? new LatencySummary()));

            var firstContent = rawAvailable && firstSamples.Count > 0
                ? LatencyStatistics.From(firstSamples).ToSummary()
                : MergeSummaries(members.ConvertAll(m => m.Cell.FirstContentByScenario.GetValueOrDefault(m.Scenario) ?? new LatencySummary()));

            if (rawAvailable)
            {
                latency.Method = "nearest-rank over every repeat's samples";
                firstContent.Method = latency.Method;

                // 🚨 A repeat whose OWN sample count sits below the floor is
                // flagged even when the merged count clears it. Three repeats
                // of 58 samples make 174, which looks safe and is not: no
                // single window ever supported a p95.
                var thinnest = perRepeatCounts.Count == 0 ? 0 : perRepeatCounts.Min();
                latency.ThinnestRepeat = thinnest;
                firstContent.ThinnestRepeat = thinnest;
            }

            // 🚨 A mixed cell's throughput is the CELL's, not each scenario's.
            // Writing 6.94/s onto all three rows of a soak makes it look as
            // though each path alone reached what the three shared, and invites
            // comparison with a sweep row that measured one path on its own.
            var throughputs = members.ConvertAll(static m =>
                m.Cell.Scenarios.Count <= 1
                    ? m.Cell.ThroughputPerSecond
                    : m.Cell.ThroughputPerSecond / m.Cell.Scenarios.Count);

            var sharedCell = members.Exists(static m => m.Cell.Scenarios.Count > 1);

            // Same reason for storage: growth measured across a MIXED window
            // belongs to the mix, not to any one path in it. Attributing it to
            // each scenario would publish the soak's 16.35 rows/run beside the
            // sweep's 27.6 for streaming and 10.5 for buffered, as though a
            // third measurement disagreed with both.
            var storage = sharedCell
                ? []
                : members.ConvertAll(static m => m.Cell.Storage).FindAll(static s => s.Available);

            // 🚨 Bytes and rows are filtered DIFFERENTLY, and that asymmetry is
            // the point. `pg_total_relation_size` counts bloat, so a window
            // autovacuum ran inside cannot contribute a byte number. A row
            // count is immune to bloat, so excluding it too would throw away
            // the one number that survives - and rows are half of what makes
            // write amplification readable.
            var cleanStorage = storage.FindAll(static s => !s.VacuumInterference);


            rows.Add(new SummaryRow
            {
                Scenario = group.Key.Scenario,
                SeedShape = group.Key.SeedShape,
                Concurrency = group.Key.Concurrency,
                ArrivalRatePerSecond = group.Key.ArrivalRatePerSecond,
                WorkerCount = group.Key.WorkerCount,
                Repeats = members.Count,
                CompleteRepeats = members.Count(static m => string.Equals(m.Cell.Status, CellStatus.Complete, StringComparison.Ordinal)),
                StorageRepeats = cleanStorage.Count,
                Latency = latency,
                FirstContent = firstContent,
                ThroughputPerSecond = throughputs.Count == 0 ? 0 : Math.Round(throughputs.Average(), 4),
                ThroughputSpread = throughputs.Count == 0 ? 0 : Math.Round(throughputs.Max() - throughputs.Min(), 4),
                BytesPerRun = Average(cleanStorage.ConvertAll(static s => s.BytesPerRun)),
                RowsPerRun = Average(storage.ConvertAll(static s => s.RowsPerRun)),
                BytesPerEvent = Average(cleanStorage.ConvertAll(static s => s.BytesPerEvent)),
                VacuumInterference = members.Exists(static m => m.Cell.Storage.VacuumInterference),
                LargestWorkerShare = Average(members.ConvertAll(static m => m.Cell.Workers?.LargestShare)),
                AttemptRatio = Average(members.ConvertAll(static m => m.Cell.Workers?.AttemptRatio)),
                ContentionMeasured = members.Exists(static m => m.Cell.Workers is not null)
                    ? members.TrueForAll(static m => m.Cell.Workers?.ContentionMeasured != false)
                    : null,
            });
        }

        rows.Sort(static (left, right) =>
        {
            var scenario = string.CompareOrdinal(left.Scenario, right.Scenario);
            if (scenario != 0)
            {
                return scenario;
            }
            var seed = string.CompareOrdinal(left.SeedShape, right.SeedShape);
            if (seed != 0)
            {
                return seed;
            }
            var workers = Nullable.Compare(left.WorkerCount, right.WorkerCount);
            if (workers != 0)
            {
                return workers;
            }
            var rate = Nullable.Compare(left.ArrivalRatePerSecond, right.ArrivalRatePerSecond);
            return rate != 0 ? rate : left.Concurrency.CompareTo(right.Concurrency);
        });

        return rows;
    }

    private static LatencySummary MergeSummaries(List<LatencySummary> summaries)
    {
        var withSamples = summaries.FindAll(static s => s.Count > 0);

        if (withSamples.Count == 0)
        {
            return new LatencySummary();
        }

        var total = withSamples.Sum(static s => s.Count);

        // A count-weighted mean of each percentile is the honest reduction
        // available from already-summarised cells: the alternative - a plain
        // average - would let a repeat with three samples weigh as much as one
        // with three thousand.
        double? Weighted(Func<LatencySummary, double?> selector)
        {
            double sum = 0;
            var weight = 0;

            foreach (var summary in withSamples)
            {
                if (selector(summary) is { } value)
                {
                    sum += value * summary.Count;
                    weight += summary.Count;
                }
            }

            return weight == 0 ? null : Math.Round(sum / weight, 3);
        }

        return new LatencySummary
        {
            Count = total,
            Minimum = withSamples.Min(static s => s.Minimum),
            Mean = Weighted(static s => s.Mean),
            P50 = Weighted(static s => s.P50),
            P95 = Weighted(static s => s.P95),
            P99 = Weighted(static s => s.P99),
            Maximum = withSamples.Max(static s => s.Maximum),
            LowSampleP95 = total < LatencyStatistics.P95SampleFloor,
            LowSampleP99 = total < LatencyStatistics.P99SampleFloor,
            Method = "nearest-rank per cell, count-weighted across repeats",
        };
    }

    private static double? Average(List<double?> values)
    {
        var present = values.FindAll(static v => v is not null);
        return present.Count == 0 ? null : Math.Round(present.Sum(static v => v!.Value) / present.Count, 4);
    }

    /// <summary>How much extra throughput a doubled load must buy to count as scaling.</summary>
    /// <remarks>
    /// 🚨 Below this, a higher offered load is buying queue depth, not work.
    /// The value is deliberately generous: 15% of a step is far less than the
    /// step itself, so a genuine (if sublinear) gain is never called a ceiling.
    /// </remarks>
    private const double ScalingFloor = 0.15;

    /// <summary>How much latency must grow at flat throughput before the plateau is a ceiling.</summary>
    private const double QueueingFloor = 0.5;

    private static string DescribeBottleneck(List<CellResult> cells, List<SummaryRow> rows)
    {
        if (cells.Count == 0)
        {
            return "not determined: the run produced no cell";
        }

        var saturated = cells.FindAll(static c => c.Arrival.Rejected > 0 || c.Arrival.TimedOut > 0 || c.Arrival.NotSent > 0);

        if (saturated.Count == 0)
        {
            // 🚨 A refusal is not the only way a ceiling shows itself, and for a
            // queue it is the LEAST likely way. A closed-loop client is never
            // rejected - it simply waits longer, so the queue's limit appears as
            // latency climbing while throughput stays flat. Counting only 429s
            // and timeouts would report "no ceiling" over exactly the ceiling
            // this apparatus exists to find.
            return DescribePlateau(rows) ?? "no ceiling was found inside the measured range";
        }

        var first = saturated[0];

        if (first.Arrival.NotSent > 0)
        {
            return $"the driver ran out of in-flight slots first in {first.CellId}; this is a client-side limit and is not reported as server capacity";
        }

        var hostCpu = first.Processes.Find(static p => string.Equals(p.Role, "host", StringComparison.Ordinal));
        var driverCpu = first.Processes.Find(static p => string.Equals(p.Role, "driver", StringComparison.Ordinal));

        if (hostCpu is null || driverCpu is null)
        {
            return $"saturation appeared in {first.CellId}, but the process telemetry needed to attribute it was not available";
        }

        return hostCpu.ProcessorSeconds > driverCpu.ProcessorSeconds
            ? $"saturation appeared in {first.CellId}; the host consumed more processor time than the driver, so the limit is on the server side of this topology"
            : $"saturation appeared in {first.CellId}, but the driver consumed more processor time than the host — this is not reported as server capacity";
    }

    /// <summary>Finds the load point where more offered load stopped buying work.</summary>
    /// <param name="rows">The merged load points.</param>
    /// <returns>The description, or <see langword="null"/> when nothing plateaued.</returns>
    private static string? DescribePlateau(List<SummaryRow> rows)
    {
        foreach (var group in rows
            .FindAll(static r => r.WorkerCount is null && r.ArrivalRatePerSecond is null)
            .GroupBy(static r => (r.Scenario, r.SeedShape)))
        {
            var steps = group.OrderBy(static r => r.Concurrency).ToList();

            for (var index = 1; index < steps.Count; index++)
            {
                var previous = steps[index - 1];
                var current = steps[index];

                if (previous.ThroughputPerSecond <= 0 || previous.Latency.P50 is not { } beforeLatency
                    || current.Latency.P50 is not { } afterLatency)
                {
                    continue;
                }

                var throughputGain = (current.ThroughputPerSecond - previous.ThroughputPerSecond) / previous.ThroughputPerSecond;
                var latencyGrowth = (afterLatency - beforeLatency) / beforeLatency;

                if (throughputGain < ScalingFloor && latencyGrowth > QueueingFloor)
                {
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "`{0}` ({1} database) stopped scaling between concurrency {2} and {3}: throughput moved "
                        + "{4:0.##}/s → {5:0.##}/s ({6:P0}) while p50 latency grew {7} ms → {8} ms ({9:P0}). "
                        + "The extra offered load bought queue depth, not work. No request was refused, "
                        + "so this ceiling is invisible to a rejection count.",
                        previous.Scenario,
                        previous.SeedShape,
                        previous.Concurrency,
                        current.Concurrency,
                        previous.ThroughputPerSecond,
                        current.ThroughputPerSecond,
                        throughputGain,
                        LatencyStatistics.Format(beforeLatency),
                        LatencyStatistics.Format(afterLatency),
                        latencyGrowth);
                }
            }
        }

        return null;
    }

    private static List<string> BuildCaveats(List<CellResult> cells, RunManifest? manifest)
    {
        var caveats = new List<string>
        {
            manifest?.Disclaimer
                ?? "Measured on one machine, one database and one configuration. This is not an SLA and not a guaranteed capacity.",
            "One machine, one database, one clock. Network partition, clock skew and inter-machine latency are out of scope, so nothing here says anything about multi-node behaviour.",
        };

        if (cells.Exists(static c => c.Storage.VacuumInterference))
        {
            caveats.Add("Autovacuum ran inside at least one window; those cells' byte growth is flagged and excluded from the averages. Their row counts remain usable.");
        }

        if (cells.Exists(static c => c.Storage.RetentionEnabled))
        {
            caveats.Add("Retention was active while at least one cell ran; its storage growth answers a different question than the retention-off measurement.");
        }

        if (cells.Exists(static c => c.Workers is { ContentionMeasured: false, WorkerCount: > 1 }))
        {
            caveats.Add("At one or more worker counts a single worker took nearly every job, so contention could not be measured there and no scaling claim is made.");
        }

        foreach (var cell in cells.FindAll(static c => !string.Equals(c.Status, CellStatus.Complete, StringComparison.Ordinal)))
        {
            caveats.Add($"{cell.CellId}: {cell.Status} — {string.Join("; ", cell.StatusReasons)}");
        }

        return caveats;
    }

    private static string RenderMarkdown(RunSummary summary, RunManifest? manifest, List<CellResult> cells)
    {
        var text = new StringBuilder();

        text.Append("# Capacity report — ").Append(summary.RunId).AppendLine();
        text.AppendLine();
        text.Append("Profile: `").Append(summary.Profile).AppendLine("`.");
        text.Append(summary.Cells).Append(" cell(s): ")
            .Append(summary.CompleteCells).Append(" complete, ")
            .Append(summary.IncompleteCells).Append(" incomplete, ")
            .Append(summary.InvalidCells).AppendLine(" invalid.");
        text.AppendLine();

        if (manifest is not null)
        {
            text.AppendLine("## Environment");
            text.AppendLine();
            text.AppendLine("| Field | Value |");
            text.AppendLine("|---|---|");
            Row(text, "Commit", manifest.Commit + (manifest.Dirty ? " (working tree dirty)" : ""));
            Row(text, "Tracon package", manifest.PackageVersion);
            Row(text, "Operating system", manifest.OperatingSystem);
            Row(text, "Architecture", manifest.Architecture);
            Row(text, "Logical processors", manifest.ProcessorCount.ToString(CultureInfo.InvariantCulture));
            Row(text, "Physical memory", StorageAccountant.FormatBytes(manifest.PhysicalMemoryBytes));
            Row(text, ".NET runtime", manifest.RuntimeVersion);
            Row(text, "PostgreSQL", manifest.PostgreSqlVersion);
            Row(text, "Database image", manifest.DatabaseImage ?? "—");
            text.AppendLine();
        }

        text.AppendLine("## Load points");
        text.AppendLine();
        text.AppendLine("| Scenario | Seed | Workers | Rate/s | Concurrency | Repeats | n | p50 ms | p95 ms | p99 ms | Throughput/s | Bytes/run | Rows/run |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");

        foreach (var row in summary.Rows)
        {
            text.Append("| ").Append(row.Scenario)
                .Append(" | ").Append(row.SeedShape)
                .Append(" | ").Append(row.WorkerCount?.ToString(CultureInfo.InvariantCulture) ?? "—")
                .Append(" | ").Append(row.ArrivalRatePerSecond?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—")
                .Append(" | ").Append(row.Concurrency.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(row.Repeats.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(row.Latency.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(LatencyStatistics.Format(row.Latency.P50))
                .Append(" | ").Append(LatencyStatistics.Format(row.Latency.P95)).Append(Warn(row.Latency, LatencyStatistics.P95SampleFloor, row.Latency.LowSampleP95))
                .Append(" | ").Append(LatencyStatistics.Format(row.Latency.P99)).Append(Warn(row.Latency, LatencyStatistics.P99SampleFloor, row.Latency.LowSampleP99))
                .Append(" | ").Append(row.ThroughputPerSecond.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(" | ").Append(Bytes(row))
                .Append(" | ").Append(row.RowsPerRun?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—")
                .AppendLine(" |");
        }

        text.AppendLine();
        text.AppendLine(
            "⚠ marks a percentile computed from fewer samples than its floor (p95: 100, p99: 1000). "
            + "⚠repeat marks one where the MERGED count clears the floor but the thinnest single repeat does not — "
            + "the number is honest, but no individual window supported it. "
            + "Percentiles are nearest rank over every repeat's samples merged together, never an average of the "
            + "repeats' own percentiles. A bytes-per-run cell says how many repeats it came from when that is fewer "
            + "than the repeat count; `1 rpt` is one window, not an average.");
        text.AppendLine();

        AppendArrival(text, cells);
        AppendStorage(text, cells);
        AppendWorkers(text, summary, cells);
        AppendTelemetry(text, cells);

        text.AppendLine("## First bottleneck");
        text.AppendLine();
        text.AppendLine(summary.Bottleneck);
        text.AppendLine();

        text.AppendLine("## What this report does not say");
        text.AppendLine();

        foreach (var caveat in summary.Caveats)
        {
            text.Append("- ").AppendLine(caveat);
        }

        return text.ToString();
    }

    private static void AppendArrival(StringBuilder text, List<CellResult> cells)
    {
        text.AppendLine("## What became of every request");
        text.AppendLine();
        text.AppendLine(
            "`planned = not sent + sent` and `sent = accepted + rejected + failed + timed out` both close on every "
            + "row; a report that cannot close them is hiding dropped load. A refusal or a timeout is a saturation "
            + "finding and stays inside `sent` - it never leaves the distribution.");
        text.AppendLine();
        text.AppendLine("| Cell | Rate/s | Planned | Sent | Not sent | Accepted | Rejected | Timed out | Completed | Drain s | Queue wait p95 ms |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");

        foreach (var cell in cells)
        {
            var arrival = cell.Arrival;

            text.Append("| ").Append(cell.CellId)
                .Append(" | ").Append(cell.ArrivalRatePerSecond?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—")
                .Append(" | ").Append(arrival.Planned.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(arrival.Sent.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(arrival.NotSent.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(arrival.Accepted.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(arrival.Rejected.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(arrival.TimedOut.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(arrival.Completed.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(cell.DrainSeconds.ToString("0.##", CultureInfo.InvariantCulture))
                .Append(" | ").Append(LatencyStatistics.Format(cell.QueueWait.P95))
                .AppendLine(" |");
        }

        text.AppendLine();
        text.AppendLine("## Reconciliation and resources");
        text.AppendLine();
        text.AppendLine(
            "A store that refuses a write leaves the run green, so counting HTTP successes alone could report a clean "
            + "measurement over lost data. Every cell therefore compares what was sent against what the store holds.");
        text.AppendLine();
        text.AppendLine("| Cell | Accepted runs | Terminal | Missing | Content mismatch | Sequence gaps | Live subs | Tenant bleed | Cross-tenant refused | Host peak RSS | Host CPU s |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");

        foreach (var cell in cells)
        {
            var reconciliation = cell.Reconciliation;
            var host = cell.Processes.Find(static p => string.Equals(p.Role, "host", StringComparison.Ordinal));

            text.Append("| ").Append(cell.CellId)
                .Append(" | ").Append(reconciliation.AcceptedRunIds.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(reconciliation.TerminalRuns.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(reconciliation.MissingRuns.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(reconciliation.ContentMismatches.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(reconciliation.SequenceGaps.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(reconciliation.LiveSubscriptions.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(reconciliation.TenantBleed.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(reconciliation.CrossTenantRefusals.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(StorageAccountant.FormatBytes(host?.PeakRssBytes))
                .Append(" | ").Append(host?.ProcessorSeconds.ToString("0.#", CultureInfo.InvariantCulture) ?? "—")
                .AppendLine(" |");
        }

        text.AppendLine();
    }

    private static void AppendStorage(StringBuilder text, List<CellResult> cells)
    {
        var withStorage = cells.FindAll(static c => c.Storage.Available && c.Storage.Tables.Count > 0);

        if (withStorage.Count == 0)
        {
            return;
        }

        text.AppendLine("## Write amplification");
        text.AppendLine();
        text.AppendLine("Rows and bytes together, table by table, with index and TOAST split out from the heap.");
        text.AppendLine();
        text.AppendLine("| Cell | Table | Rows | Heap | Index | TOAST | Total | Autovacuum |");
        text.AppendLine("|---|---|---|---|---|---|---|---|");

        foreach (var cell in withStorage)
        {
            foreach (var table in cell.Storage.Tables)
            {
                if (table.Rows == 0 && table.TotalBytes == 0)
                {
                    continue;
                }

                text.Append("| ").Append(cell.CellId)
                    .Append(" | `").Append(table.Table).Append('`')
                    .Append(" | ").Append(table.Rows.ToString(CultureInfo.InvariantCulture))
                    .Append(" | ").Append(StorageAccountant.FormatBytes(table.HeapBytes))
                    .Append(" | ").Append(StorageAccountant.FormatBytes(table.IndexBytes))
                    .Append(" | ").Append(StorageAccountant.FormatBytes(table.ToastBytes))
                    .Append(" | ").Append(StorageAccountant.FormatBytes(table.TotalBytes))
                    .Append(" | ").Append(table.Vacuumed ? "ran" : "—")
                    .AppendLine(" |");
            }
        }

        text.AppendLine();
    }

    private static void AppendWorkers(StringBuilder text, RunSummary summary, List<CellResult> cells)
    {
        var withWorkers = cells.FindAll(static c => c.Workers is not null);

        if (withWorkers.Count == 0)
        {
            return;
        }

        text.AppendLine("## Worker contention");
        text.AppendLine();
        text.AppendLine(
            "The worker count is a separate axis: the offered load is held constant and only the number of leasing processes changes. " +
            "🚨 This measures contention on one machine, one database and one clock. It is **not** a statement that multiple nodes are supported.");
        text.AppendLine();
        text.AppendLine("| Workers | Throughput/s | Largest share | Attempts / accepted job | Overlaps | Contention measured |");
        text.AppendLine("|---|---|---|---|---|---|");

        foreach (var row in summary.Rows.FindAll(static r => r.WorkerCount is not null))
        {
            var overlaps = withWorkers
                .FindAll(c => c.WorkerCount == row.WorkerCount)
                .Sum(static c => c.Workers!.ConcurrentOverlaps);

            text.Append("| ").Append(row.WorkerCount!.Value.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(row.ThroughputPerSecond.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(" | ").Append(row.LargestWorkerShare?.ToString("P1", CultureInfo.InvariantCulture) ?? "—")
                .Append(" | ").Append(row.AttemptRatio?.ToString("0.###", CultureInfo.InvariantCulture) ?? "—")
                .Append(" | ").Append(overlaps.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(row.ContentionMeasured == true ? "yes" : "no")
                .AppendLine(" |");
        }

        text.AppendLine();
        text.AppendLine("Per worker process, per cell:");
        text.AppendLine();
        text.AppendLine("| Cell | Worker | PID | Jobs | Attempts |");
        text.AppendLine("|---|---|---|---|---|");

        foreach (var cell in withWorkers)
        {
            foreach (var share in cell.Workers!.Shares)
            {
                text.Append("| ").Append(cell.CellId)
                    .Append(" | ").Append(share.Name)
                    .Append(" | ").Append(share.Pid.ToString(CultureInfo.InvariantCulture))
                    .Append(" | ").Append(share.Jobs.ToString(CultureInfo.InvariantCulture))
                    .Append(" | ").Append(share.Attempts.ToString(CultureInfo.InvariantCulture))
                    .AppendLine(" |");
            }
        }

        text.AppendLine();
    }

    private static void AppendTelemetry(StringBuilder text, List<CellResult> cells)
    {
        text.AppendLine("## Telemetry coverage");
        text.AppendLine();

        var unavailable = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var cell in cells)
        {
            foreach (var (name, reason) in cell.Telemetry.Unavailable)
            {
                unavailable[name] = reason;
            }
        }

        if (unavailable.Count == 0)
        {
            text.AppendLine("Every measurement this apparatus knows how to take was available.");
            text.AppendLine();
            return;
        }

        text.AppendLine("These measurements were **unavailable** and are written as such, never as zero:");
        text.AppendLine();

        foreach (var (name, reason) in unavailable)
        {
            text.Append("- `").Append(name).Append("`: ").AppendLine(reason);
        }

        text.AppendLine();
    }

    private static string Warn(LatencySummary latency, int floor, bool belowFloor)
    {
        if (belowFloor)
        {
            return " ⚠";
        }

        // 🚨 The merged count can clear the floor while no single window did.
        return latency.ThinnestRepeat is { } thinnest && thinnest > 0 && thinnest < floor ? " ⚠repeat" : "";
    }

    private static string Bytes(SummaryRow row)
    {
        var formatted = StorageAccountant.FormatBytes(row.BytesPerRun);

        if (row.BytesPerRun is null)
        {
            return row.VacuumInterference ? "— (vacuum)" : formatted;
        }

        // 🚨 One clean repeat is not an average, and the phase's own rule
        // forbids publishing it as though it were.
        return row.StorageRepeats < row.Repeats
            ? string.Create(CultureInfo.InvariantCulture, $"{formatted} ({row.StorageRepeats} rpt)")
            : formatted;
    }

    private static void Row(StringBuilder text, string field, string value)
        => text.Append("| ").Append(field).Append(" | ").Append(value).AppendLine(" |");

    private static void WriteCharts(string runDirectory, RunSummary summary)
    {
        var charts = Path.Combine(runDirectory, "charts");
        Directory.CreateDirectory(charts);

        var latency = summary.Rows.FindAll(static r => r.Latency.Count > 0);

        if (latency.Count > 0)
        {
            File.WriteAllText(
                Path.Combine(charts, "load-latency.svg"),
                SvgChart.Render(
                    "Load vs p95 latency (ms)",
                    latency.ConvertAll(static r => (
                        Label: r.WorkerCount is { } w
                            ? w.ToString(CultureInfo.InvariantCulture) + "w"
                            : r.Concurrency.ToString(CultureInfo.InvariantCulture),
                        Value: r.Latency.P95 ?? 0))));

            File.WriteAllText(
                Path.Combine(charts, "load-throughput.svg"),
                SvgChart.Render(
                    "Load vs throughput (completed/s)",
                    latency.ConvertAll(static r => (
                        Label: r.WorkerCount is { } w
                            ? w.ToString(CultureInfo.InvariantCulture) + "w"
                            : r.Concurrency.ToString(CultureInfo.InvariantCulture),
                        Value: r.ThroughputPerSecond))));
        }
    }
}
