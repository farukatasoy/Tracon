using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Tracon.CapacityDriver;

/// <summary>Reads a cell's footprint files, analyses them and writes the result as JSON, CSV and Markdown.</summary>
public static class FootprintReport
{
    /// <summary>The file the sampler writes, one <see cref="FootprintSample"/> per line.</summary>
    public const string SamplesFile = "footprint.jsonl";

    /// <summary>The file the runner writes: the ramp as it ran.</summary>
    public const string RampFile = "ramp.json";

    /// <summary>Whether a cell directory holds a footprint to analyse.</summary>
    /// <param name="cellDirectory">The cell's directory.</param>
    /// <returns><see langword="true"/> when both input files exist.</returns>
    public static bool Exists(string cellDirectory)
        => File.Exists(Path.Combine(cellDirectory, SamplesFile)) && File.Exists(Path.Combine(cellDirectory, RampFile));

    /// <summary>Analyses a cell directory and writes <c>footprint.json</c>, <c>footprint.csv</c> and <c>footprint.md</c> beside the inputs.</summary>
    /// <param name="cellDirectory">The cell's directory.</param>
    /// <param name="options">The thresholds; the defaults when <see langword="null"/>.</param>
    /// <returns>The analysis.</returns>
    public static FootprintAnalysisResult Write(string cellDirectory, FootprintAnalysisOptions? options = null)
    {
        var samples = ReadSamples(Path.Combine(cellDirectory, SamplesFile));
        var ramp = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(cellDirectory, RampFile)),
            CapacityJson.Default.RampRecord)
            ?? throw new InvalidOperationException("ramp.json does not hold a ramp record.");

        var cellId = samples.Count > 0 ? samples[0].CellId : Path.GetFileName(cellDirectory);
        var effective = options ?? new FootprintAnalysisOptions { BucketSeconds = ramp.Spec.BucketSeconds };
        var analysis = FootprintAnalysis.Analyse(cellId, samples, ramp, effective);

        File.WriteAllText(
            Path.Combine(cellDirectory, "footprint.json"),
            Redactor.Scrub(JsonSerializer.Serialize(analysis, CapacityJson.Default.FootprintAnalysisResult)));
        File.WriteAllText(Path.Combine(cellDirectory, "footprint.csv"), Csv(samples));
        File.WriteAllText(Path.Combine(cellDirectory, "footprint.md"), Markdown(analysis));

        return analysis;
    }

    /// <summary>Reads a samples file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The samples in file order.</returns>
    public static List<FootprintSample> ReadSamples(string path)
    {
        var samples = new List<FootprintSample>();

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (JsonSerializer.Deserialize(line, CapacityJson.Default.FootprintSample) is { } sample)
            {
                samples.Add(sample);
            }
        }

        return samples;
    }

    /// <summary>The samples as one wide table, one row per sample.</summary>
    /// <param name="samples">The samples.</param>
    /// <returns>CSV text.</returns>
    public static string Csv(IReadOnlyList<FootprintSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var columns = samples
            .SelectMany(static s => s.Values.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToList();

        var text = new StringBuilder();
        text.Append("offsetSeconds,stage,phase,tenants,issued,collected");

        foreach (var column in columns)
        {
            text.Append(',').Append(column);
        }

        text.AppendLine();

        foreach (var sample in samples)
        {
            text.Append(sample.OffsetSeconds.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(',').Append(sample.Stage.ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(sample.Phase)
                .Append(',').Append(sample.Tenants.ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(sample.Issued.ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(sample.Collected ? '1' : '0');

            foreach (var column in columns)
            {
                text.Append(',');

                // An absent reading is an empty cell, never a zero.
                if (sample.Values.TryGetValue(column, out var value))
                {
                    text.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
                }
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    /// <summary>The analysis as a report.</summary>
    /// <param name="analysis">The analysis.</param>
    /// <returns>Markdown text.</returns>
    public static string Markdown(FootprintAnalysisResult analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var text = new StringBuilder();
        var ramp = analysis.Ramp;

        text.AppendLine(CultureInfo.InvariantCulture, $"# Tenant footprint - {analysis.CellId}");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Verdict: **{analysis.Verdict.ToUpperInvariant()}**. {analysis.Samples} samples, {analysis.Gaps} with a source missing.");
        text.AppendLine();

        text.AppendLine("## What was run");
        text.AppendLine();
        text.AppendLine("| Stage | Tenants | Joined | Runs each | Plateau s | Plateau window s | Requests/s |");
        text.AppendLine("|---|---|---|---|---|---|---|");

        for (var index = 0; index < ramp.Spec.Stages.Count; index++)
        {
            var stage = ramp.Spec.Stages[index];
            var hold = ramp.Marks.FirstOrDefault(m => m.Stage == index && string.Equals(m.Phase, RampPhase.Hold, StringComparison.Ordinal));
            var previous = index == 0 ? 0 : ramp.Spec.Stages[index - 1].Tenants;
            var rate = hold is { EndSeconds: > 0 } && hold.EndSeconds > hold.StartSeconds
                ? (hold.IssuedAtEnd - hold.IssuedAtStart) / (hold.EndSeconds - hold.StartSeconds)
                : 0;

            text.AppendLine(CultureInfo.InvariantCulture, $"| {index} | {stage.Tenants} | {stage.Tenants - previous} | {stage.RunsPerNewTenant} | {stage.HoldSeconds:0.#} | {(hold is null ? "-" : $"{hold.StartSeconds:0}-{hold.EndSeconds:0}")} | {rate:0.0} |");
        }

        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Idle {ramp.Spec.IdleSeconds:0.#} s at the end. A full collection was forced every {ramp.Spec.CollectIntervalSeconds:0.#} s (and once more at the end), so the live-heap series has that cadence. {ramp.BaselineTenants} tenants (the warm-up's) were known to the host before stage 0.");
        text.AppendLine();

        text.AppendLine("## Verdict by metric");
        text.AppendLine();
        text.AppendLine("| Metric | Verdict | Why |");
        text.AppendLine("|---|---|---|");

        foreach (var metric in Judged(analysis))
        {
            var why = metric.Reasons.Count == 0
                ? (string.Equals(metric.Verdict, FootprintVerdict.Inconclusive, StringComparison.Ordinal) ? "not enough points in any plateau" : "")
                : string.Join("; ", metric.Reasons);

            text.AppendLine(CultureInfo.InvariantCulture, $"| {metric.Label} | {metric.Verdict} | {why.Replace("|", "/", StringComparison.Ordinal)} |");
        }

        text.AppendLine();
        text.AppendLine("## Level at each plateau, and what the tenants cost");
        text.AppendLine();
        text.AppendLine("Level is the median of the last third of the plateau's window. Cost per tenant is the step between two levels divided by the tenants added in between; it includes whatever the runs of that stage cost, so read it together with the drift table. Retained is the idle level less the first plateau's, per tenant added since: what the process still holds once nothing is using it.");
        text.AppendLine();

        var header = new StringBuilder("| Metric |");
        var divider = new StringBuilder("|---|");

        for (var index = 0; index < ramp.Spec.Stages.Count; index++)
        {
            header.Append(CultureInfo.InvariantCulture, $" {ramp.Spec.Stages[index].Tenants} tenants |");
            divider.Append("---|");
        }

        header.Append(" Idle | Per tenant, by step | Retained per tenant |");
        divider.Append("---|---|---|");
        text.AppendLine(header.ToString());
        text.AppendLine(divider.ToString());

        foreach (var metric in Judged(analysis).Where(static m => !m.Name.StartsWith(FootprintCatalog.SeriesPrefix, StringComparison.Ordinal)))
        {
            var row = new StringBuilder($"| {metric.Label} |");

            foreach (var level in metric.Levels)
            {
                row.Append(CultureInfo.InvariantCulture, $" {(level is { } value ? FootprintAnalysis.Format(metric.Kind, value) : "-")} |");
            }

            for (var pad = metric.Levels.Count; pad < ramp.Spec.Stages.Count; pad++)
            {
                row.Append(" - |");
            }

            row.Append(CultureInfo.InvariantCulture, $" {(metric.IdleLevel is { } idle ? FootprintAnalysis.Format(metric.Kind, idle) : "-")} |");
            row.Append(' ').Append(string.Join(", ", metric.Steps.Select(s => s.Delta >= s.Floor ? FootprintAnalysis.Format(metric.Kind, s.PerTenant) : "~0"))).Append(" |");
            row.Append(CultureInfo.InvariantCulture, $" {(metric.RetainedPerTenant is { } retained ? FootprintAnalysis.Format(metric.Kind, retained) : "-")} |");
            text.AppendLine(row.ToString());
        }

        text.AppendLine();
        text.AppendLine("## Drift inside each plateau");
        text.AppendLine();
        text.AppendLine("Growth is the Theil-Sen slope carried across the plateau's window. Floor is the smallest growth that would have counted, so a flat row says how small a leak it could not have seen. `-` is a plateau with too few points to judge.");
        text.AppendLine();
        text.AppendLine("| Metric | Stage | Tenants | Points | First third | Last third | Growth | Floor | tau | p | Verdict |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");

        foreach (var metric in Judged(analysis).Where(static m => !m.Name.StartsWith(FootprintCatalog.SeriesPrefix, StringComparison.Ordinal)))
        {
            foreach (var trend in metric.Trends)
            {
                var judged = !string.Equals(trend.Verdict, FootprintVerdict.Inconclusive, StringComparison.Ordinal);

                text.AppendLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"| {metric.Label} | {trend.Stage} | {trend.Tenants} | {trend.Points} | {(judged ? FootprintAnalysis.Format(metric.Kind, trend.FirstThird) : "-")} | {(judged ? FootprintAnalysis.Format(metric.Kind, trend.LastThird) : "-")} | {(judged ? FootprintAnalysis.Format(metric.Kind, trend.Growth) : "-")} | {FootprintAnalysis.Format(metric.Kind, trend.Floor)} | {(judged ? trend.Tau.ToString("0.00", CultureInfo.InvariantCulture) : "-")} | {(judged ? trend.PValue.ToString("0.0000", CultureInfo.InvariantCulture) : "-")} | {trend.Verdict} |"));
            }
        }

        var grown = Judged(analysis)
            .Where(static m => m.Name.StartsWith(FootprintCatalog.SeriesPrefix, StringComparison.Ordinal) && m.Maximum > m.Minimum)
            .OrderByDescending(static m => m.Maximum - m.Minimum)
            .Take(15)
            .ToList();

        text.AppendLine();
        text.AppendLine("## Metric series per instrument");
        text.AppendLine();

        if (grown.Count == 0)
        {
            text.AppendLine("No instrument's series count changed between the first and last reading.");
        }
        else
        {
            text.AppendLine("Distinct tag sets seen per instrument, for the instruments whose count changed. A count that follows the tenants means the instrument carries a tenant tag.");
            text.AppendLine();
            text.AppendLine("| Instrument | Min | Max | Verdict | Why |");
            text.AppendLine("|---|---|---|---|---|");

            foreach (var metric in grown)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"| {metric.Label["Series of ".Length..]} | {metric.Minimum:0} | {metric.Maximum:0} | {metric.Verdict} | {string.Join("; ", metric.Reasons).Replace("|", "/", StringComparison.Ordinal)} |");
            }
        }

        text.AppendLine();
        text.AppendLine("## What this does not say");
        text.AppendLine();
        text.AppendLine("- One machine, one runtime, one database, one workload (three scenarios mixed, a synthetic model). It says nothing about a different host configuration.");
        text.AppendLine("- Mann-Kendall assumes independent points; window medians of a process counter are not. The p-value is therefore never the whole verdict: a plateau is red only when it is also above the floor and its last third sits above its first.");
        text.AppendLine("- A flat plateau means no growth above its floor. A short plateau cannot see a slow leak; the floor column says how slow.");
        text.AppendLine("- The forced collections pause the process. Latency measured in the same cell is not a latency finding.");
        text.AppendLine("- Tenant ids are well-formed but synthetic and arrive in the header the loopback fixture resolves; nothing here measures real identity resolution.");

        return text.ToString();
    }

    private static IEnumerable<MetricAnalysis> Judged(FootprintAnalysisResult analysis)
        => analysis.Metrics.Where(static m => !m.Name.StartsWith(FootprintCatalog.SeriesPrefix, StringComparison.Ordinal))
            .Concat(analysis.Metrics.Where(static m => m.Name.StartsWith(FootprintCatalog.SeriesPrefix, StringComparison.Ordinal)));
}
