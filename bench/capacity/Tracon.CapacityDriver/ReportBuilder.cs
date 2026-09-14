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

    /// <summary>What the first bottleneck was, or why it could not be determined.</summary>
    public string Bottleneck { get; set; } = "not determined";

    /// <summary>Everything the reader must know before reading a number above.</summary>
    public List<string> Caveats { get; set; } = [];
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
        summary.Rows = BuildRows(cells);
        summary.Bottleneck = DescribeBottleneck(cells);
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

    private static RunManifest? LoadManifest(string runDirectory)
    {
        var path = Path.Combine(runDirectory, "manifest.json");

        return File.Exists(path)
            ? JsonSerializer.Deserialize(File.ReadAllText(path), CapacityJson.Default.RunManifest)
            : null;
    }

    private static List<SummaryRow> BuildRows(List<CellResult> cells)
    {
        var rows = new List<SummaryRow>();

        var groups = cells
            .Where(static c => !string.Equals(c.Status, CellStatus.Invalid, StringComparison.Ordinal))
            .SelectMany(static c => c.Scenarios.Select(scenario => (Cell: c, Scenario: scenario)))
            .GroupBy(static pair => (pair.Scenario, pair.Cell.SeedShape, pair.Cell.Concurrency, pair.Cell.ArrivalRatePerSecond, pair.Cell.WorkerCount));

        foreach (var group in groups)
        {
            var members = group.ToList();

            // 🚨 Percentiles are recomputed from the merged sample counts, never
            // averaged across repeats. The merge is approximate only in that the
            // per-cell summaries are already reduced; the count-weighted merge
            // below keeps the sample count honest, which is what the low-sample
            // warnings depend on.
            var latency = MergeSummaries(members.ConvertAll(m => m.Cell.LatencyByScenario.GetValueOrDefault(m.Scenario) ?? new LatencySummary()));
            var firstContent = MergeSummaries(members.ConvertAll(m => m.Cell.FirstContentByScenario.GetValueOrDefault(m.Scenario) ?? new LatencySummary()));

            var throughputs = members.ConvertAll(static m => m.Cell.ThroughputPerSecond);
            var storage = members.ConvertAll(static m => m.Cell.Storage).FindAll(static s => s.Available && !s.VacuumInterference);

            rows.Add(new SummaryRow
            {
                Scenario = group.Key.Scenario,
                SeedShape = group.Key.SeedShape,
                Concurrency = group.Key.Concurrency,
                ArrivalRatePerSecond = group.Key.ArrivalRatePerSecond,
                WorkerCount = group.Key.WorkerCount,
                Repeats = members.Count,
                CompleteRepeats = members.Count(static m => string.Equals(m.Cell.Status, CellStatus.Complete, StringComparison.Ordinal)),
                Latency = latency,
                FirstContent = firstContent,
                ThroughputPerSecond = throughputs.Count == 0 ? 0 : Math.Round(throughputs.Average(), 4),
                ThroughputSpread = throughputs.Count == 0 ? 0 : Math.Round(throughputs.Max() - throughputs.Min(), 4),
                BytesPerRun = Average(storage.ConvertAll(static s => s.BytesPerRun)),
                RowsPerRun = Average(storage.ConvertAll(static s => s.RowsPerRun)),
                BytesPerEvent = Average(storage.ConvertAll(static s => s.BytesPerEvent)),
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

    private static string DescribeBottleneck(List<CellResult> cells)
    {
        if (cells.Count == 0)
        {
            return "not determined: the run produced no cell";
        }

        var saturated = cells.FindAll(static c => c.Arrival.Rejected > 0 || c.Arrival.TimedOut > 0 || c.Arrival.NotSent > 0);

        if (saturated.Count == 0)
        {
            return "no ceiling was found inside the measured range";
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
        text.AppendLine("| Scenario | Seed | Workers | Rate/s | Concurrency | n | p50 ms | p95 ms | p99 ms | Throughput/s | Bytes/run | Rows/run |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");

        foreach (var row in summary.Rows)
        {
            text.Append("| ").Append(row.Scenario)
                .Append(" | ").Append(row.SeedShape)
                .Append(" | ").Append(row.WorkerCount?.ToString(CultureInfo.InvariantCulture) ?? "—")
                .Append(" | ").Append(row.ArrivalRatePerSecond?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—")
                .Append(" | ").Append(row.Concurrency.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(row.Latency.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(LatencyStatistics.Format(row.Latency.P50))
                .Append(" | ").Append(LatencyStatistics.Format(row.Latency.P95)).Append(row.Latency.LowSampleP95 ? " ⚠" : "")
                .Append(" | ").Append(LatencyStatistics.Format(row.Latency.P99)).Append(row.Latency.LowSampleP99 ? " ⚠" : "")
                .Append(" | ").Append(row.ThroughputPerSecond.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(" | ").Append(StorageAccountant.FormatBytes(row.BytesPerRun)).Append(row.VacuumInterference ? " (vacuum)" : "")
                .Append(" | ").Append(row.RowsPerRun?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—")
                .AppendLine(" |");
        }

        text.AppendLine();
        text.AppendLine("⚠ marks a percentile computed from fewer samples than its floor (p95: 100, p99: 1000).");
        text.AppendLine();

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
