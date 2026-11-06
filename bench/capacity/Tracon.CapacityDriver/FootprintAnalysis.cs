using System.Globalization;

namespace Tracon.CapacityDriver;

/// <summary>How a metric's noise is judged.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<MetricKind>))]
public enum MetricKind
{
    /// <summary>A byte count that moves with the operating system's paging and the collector's timing: resident set, committed heap.</summary>
    Bytes,

    /// <summary>A byte count read after a full compacting collection: only live objects are in it, so its noise is small.</summary>
    PreciseBytes,

    /// <summary>A count of something the process holds: threads, timers, descriptors, connections, series. It should be flat.</summary>
    Count,

    /// <summary>
    /// A count of objects that cost memory the way bytes do: one per tenant is a cost, one per run is a leak, and a
    /// cost per tenant that climbs is super-linear. Judged like <see cref="Bytes"/>, with the floors of <see cref="Count"/>.
    /// </summary>
    Objects,

    /// <summary>
    /// A count a pool keeps between a minimum and a ceiling and moves by itself with the load: the database client's
    /// connections. It is never flat, so the rule for <see cref="Count"/> reads its breathing as growth. Judged by where
    /// the plateau as a whole sits, and a rise counts only when it is larger than the pool's own breathing and the pool's
    /// lowest reading rose too; a pool that fills up is red at any size.
    /// </summary>
    Pooled,
}

/// <summary>One thing the host or the database reports about the host's footprint.</summary>
/// <param name="Name">The reading's name in <see cref="FootprintSample.Values"/>.</param>
/// <param name="Label">What a person calls it.</param>
/// <param name="Kind">How its noise is judged.</param>
public sealed record FootprintMetric(string Name, string Label, MetricKind Kind);

/// <summary>The readings the analysis knows how to judge.</summary>
public static class FootprintCatalog
{
    /// <summary>The prefix of a per-instrument metric series count.</summary>
    public const string SeriesPrefix = "series:";

    /// <summary>Every fixed reading.</summary>
    public static IReadOnlyList<FootprintMetric> Metrics { get; } =
    [
        new("host.liveHeapBytes", "Live managed heap (after full GC)", MetricKind.PreciseBytes),
        new("host.workingSetBytes", "Working set (resident)", MetricKind.Bytes),
        new("host.privateBytes", "Private bytes", MetricKind.Bytes),
        new("host.gcHeapSizeBytes", "GC heap size (last GC)", MetricKind.Bytes),
        new("host.gcCommittedBytes", "GC committed", MetricKind.Bytes),
        new("host.gcFragmentedBytes", "GC fragmented", MetricKind.Bytes),
        new("host.osThreads", "OS threads", MetricKind.Count),
        new("host.threadPoolThreads", "Thread pool threads", MetricKind.Count),
        new("host.activeTimers", "Active timers", MetricKind.Count),
        new("host.openDescriptors", "Open file descriptors", MetricKind.Count),
        new("host.handleCount", "Handles", MetricKind.Count),
        new("host.ledgerInFlight", "Runs in flight (host ledger)", MetricKind.Count),
        new("host.meterSeries", "Metric series (all instruments)", MetricKind.Count),
        new("host.meterInstruments", "Metric instruments that published", MetricKind.Count),
        new("host.meterPublications", "Instrument announcements (a meter made again counts again)", MetricKind.Objects),
        new("host.loadedAssemblies", "Loaded assemblies", MetricKind.Count),
        new("pg.connections", "PostgreSQL client connections", MetricKind.Pooled),
        new("pg.idleInTransaction", "PostgreSQL idle in transaction", MetricKind.Count),
    ];

    /// <summary>Finds how a reading should be judged.</summary>
    /// <param name="name">The reading's name.</param>
    /// <returns>The metric, or <see langword="null"/> when the analysis does not judge it.</returns>
    public static FootprintMetric? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        foreach (var metric in Metrics)
        {
            if (string.Equals(metric.Name, name, StringComparison.Ordinal))
            {
                return metric;
            }
        }

        if (name.StartsWith(SeriesPrefix, StringComparison.Ordinal))
        {
            return new FootprintMetric(name, "Series of " + name[SeriesPrefix.Length..], MetricKind.Count);
        }

        // The up-down totals the host reports (framework pool sizes, active requests) are written to the CSV and judged
        // by nobody: a memory pool's pooled bytes rise with concurrency, not with tenants, and a rule that called that
        // a leak would be wrong more often than right. Connections and descriptors are judged on their own readings.

        if (name.StartsWith("host.meterRepeatedPublications.", StringComparison.Ordinal))
        {
            return new FootprintMetric(name, "Announcements of " + name["host.meterRepeatedPublications.".Length..], MetricKind.Objects);
        }

        return null;
    }
}

/// <summary>The thresholds the verdicts use.</summary>
/// <remarks>
/// 🚨 None of these is a property of the product. Each one is how large a change has to be before this analysis
/// calls it a change rather than the process breathing, chosen from the noise of a short run on a laptop. They are
/// written down and travel in the output, so a reader can say "I would call that smaller" and recompute.
/// </remarks>
public sealed record FootprintAnalysisOptions
{
    /// <summary>The width of a window whose median stands for it, in seconds.</summary>
    public double BucketSeconds { get; init; } = 10;

    /// <summary>The share of a plateau, from its start, that is skipped as settling: joiners still finishing, the collector catching up.</summary>
    public double SettleFraction { get; init; } = 0.2;

    /// <summary>The most a plateau's settling can take, in seconds.</summary>
    public double SettleCapSeconds { get; init; } = 60;

    /// <summary>Fewest window points a plateau needs before it is judged. Fewer and the verdict is inconclusive, never green.</summary>
    public int MinimumPoints { get; init; } = 6;

    /// <summary>The Mann-Kendall significance.</summary>
    public double Alpha { get; init; } = 0.01;

    /// <summary>The smallest change of a <see cref="MetricKind.Bytes"/> metric that counts, in bytes.</summary>
    public double BytesFloor { get; init; } = 16 * 1024 * 1024;

    /// <summary>The smallest relative change of a <see cref="MetricKind.Bytes"/> metric that counts.</summary>
    public double BytesRelative { get; init; } = 0.05;

    /// <summary>The smallest change of a <see cref="MetricKind.PreciseBytes"/> metric that counts, in bytes.</summary>
    public double PreciseFloor { get; init; } = 2 * 1024 * 1024;

    /// <summary>The smallest relative change of a <see cref="MetricKind.PreciseBytes"/> metric that counts.</summary>
    public double PreciseRelative { get; init; } = 0.02;

    /// <summary>The smallest change of a <see cref="MetricKind.Count"/> metric that counts.</summary>
    public double CountFloor { get; init; } = 3;

    /// <summary>The smallest relative change of a <see cref="MetricKind.Count"/> metric that counts.</summary>
    public double CountRelative { get; init; } = 0.03;

    /// <summary>How much larger one step's cost per tenant must be than the step before it to be called super-linear.</summary>
    public double SuperLinearRatio { get; init; } = 1.5;

    /// <summary>
    /// The smallest rise of a <see cref="MetricKind.Pooled"/> metric from one plateau to the next that counts: how far a
    /// connection pool's level moves between two plateaus with nothing about the tenants changing. A run whose first plateau
    /// breathes wider than this raises it for that run.
    /// </summary>
    /// <remarks>
    /// 8, from the six <c>tenant-ramp</c> cells recorded in November 2026 (28 plateaus): the median of a plateau sat between 9
    /// and 15 connections at 2 to 10 000 tenants with no relation to the tenants, and the largest rise from one plateau to the
    /// next was 5 (7 when the end of the plateau is read instead of the whole). Inside one plateau the pool was flat to within a
    /// connection, so the first plateau alone says almost nothing about how far it moves between two.
    /// </remarks>
    public double PooledBreath { get; init; } = 8;

    /// <summary>The smallest change that counts for a metric of a kind at a level.</summary>
    /// <param name="kind">The metric's kind.</param>
    /// <param name="baseline">The level the change is measured from.</param>
    /// <returns>The floor, in the metric's unit.</returns>
    public double Floor(MetricKind kind, double baseline)
    {
        var magnitude = Math.Abs(double.IsNaN(baseline) ? 0 : baseline);

        return kind switch
        {
            MetricKind.Bytes => Math.Max(BytesFloor, BytesRelative * magnitude),
            MetricKind.PreciseBytes => Math.Max(PreciseFloor, PreciseRelative * magnitude),
            _ => Math.Max(CountFloor, CountRelative * magnitude),
        };
    }
}

/// <summary>The verdict words.</summary>
public static class FootprintVerdict
{
    /// <summary>Nothing the analysis counts as growth was found, and there was enough data to say so.</summary>
    public const string Green = "green";

    /// <summary>Growth that no reading of the invariant allows: it follows time or runs, or it grows faster than the tenants do.</summary>
    public const string Red = "red";

    /// <summary>There was not enough data to judge. Never read as green.</summary>
    public const string Inconclusive = "inconclusive";

    /// <summary>
    /// The metric rose and then did not keep rising once the load and the tenants went on: a warm-up, not a leak.
    /// Surfaced in the report, never red.
    /// </summary>
    public const string Transient = "transient";

    /// <summary>
    /// The metric moved once, by more than the floor, and was level before and after: a pool opening three more
    /// connections, a buffer doubling. A leak is a slope and shows in both halves of a plateau; a jump shows in one.
    /// Surfaced in the report, never red.
    /// </summary>
    public const string Jump = "jump";
}

/// <summary>How one metric moved inside one plateau.</summary>
public sealed class StageTrend
{
    /// <summary>The stage.</summary>
    public int Stage { get; set; }

    /// <summary>The ramp tenants active throughout the plateau.</summary>
    public int Tenants { get; set; }

    /// <summary>The window points the verdict rests on.</summary>
    public int Points { get; set; }

    /// <summary>The plateau's window, in seconds from the cell's start.</summary>
    public double WindowStartSeconds { get; set; }

    /// <summary>The end of the plateau's window.</summary>
    public double WindowEndSeconds { get; set; }

    /// <summary>The median of the first third of the window.</summary>
    public double FirstThird { get; set; }

    /// <summary>The median of the last third of the window.</summary>
    public double LastThird { get; set; }

    /// <summary>The Theil-Sen slope, per second.</summary>
    public double SlopePerSecond { get; set; }

    /// <summary>The slope carried across the window: how much the metric grew inside the plateau.</summary>
    public double Growth { get; set; }

    /// <summary>The Mann-Kendall tau.</summary>
    public double Tau { get; set; }

    /// <summary>The Mann-Kendall p-value.</summary>
    public double PValue { get; set; }

    /// <summary>The smallest growth that would have counted, so a flat verdict says how small a leak it could not see.</summary>
    public double Floor { get; set; }

    /// <summary><see cref="FootprintVerdict"/>.</summary>
    public string Verdict { get; set; } = FootprintVerdict.Inconclusive;

    /// <summary>The window's requests per second, so a growth can be read as a cost per run.</summary>
    public double RequestsPerSecond { get; set; }

    /// <summary>The growth divided by the requests issued across the window, or <see langword="null"/> when the plateau was not judged.</summary>
    public double? GrowthPerRequest { get; set; }
}

/// <summary>How one metric moved from one plateau to the next, which is the cost of the tenants added between them.</summary>
public sealed class StageStep
{
    /// <summary>The stage the step arrives at.</summary>
    public int ToStage { get; set; }

    /// <summary>The level at the plateau before.</summary>
    public double From { get; set; }

    /// <summary>The level at this plateau.</summary>
    public double To { get; set; }

    /// <summary>How many tenants joined in between.</summary>
    public int AddedTenants { get; set; }

    /// <summary>The change, in the metric's unit.</summary>
    public double Delta { get; set; }

    /// <summary>The change divided by the tenants added.</summary>
    public double PerTenant { get; set; }

    /// <summary>This step's cost per tenant over the one before it, when both are above the floor.</summary>
    public double? RatioToPrevious { get; set; }

    /// <summary>The smallest change that would have counted.</summary>
    public double Floor { get; set; }

    /// <summary><see cref="FootprintVerdict"/>.</summary>
    public string Verdict { get; set; } = FootprintVerdict.Inconclusive;

    /// <summary>One sentence on what the step is.</summary>
    public string Note { get; set; } = "";
}

/// <summary>Everything the analysis says about one metric.</summary>
public sealed class MetricAnalysis
{
    /// <summary>The reading's name.</summary>
    public string Name { get; set; } = "";

    /// <summary>What a person calls it.</summary>
    public string Label { get; set; } = "";

    /// <summary>How its noise was judged.</summary>
    public MetricKind Kind { get; set; }

    /// <summary>The worst of the plateau and step verdicts.</summary>
    public string Verdict { get; set; } = FootprintVerdict.Inconclusive;

    /// <summary>Why, one entry per finding.</summary>
    public List<string> Reasons { get; set; } = [];

    /// <summary>What rose and then stopped, so a reader can see a warm-up was seen and set aside, not missed.</summary>
    public List<string> Notes { get; set; } = [];

    /// <summary>The level at each plateau, in stage order.</summary>
    public List<double?> Levels { get; set; } = [];

    /// <summary>
    /// For a <see cref="MetricKind.Pooled"/> metric, the lowest window of each plateau, in stage order: where the pool went
    /// when the load let it.
    /// </summary>
    public List<double?> Troughs { get; set; } = [];

    /// <summary>
    /// For a <see cref="MetricKind.Pooled"/> metric, the rise from one plateau to the next that it takes to be called more
    /// than the pool breathing, or <see langword="null"/> for any other kind.
    /// </summary>
    public double? Breath { get; set; }

    /// <summary>How the metric moved inside each plateau.</summary>
    public List<StageTrend> Trends { get; set; } = [];

    /// <summary>How the metric moved between plateaus.</summary>
    public List<StageStep> Steps { get; set; } = [];

    /// <summary>The level once the load stopped and the process settled, or <see langword="null"/> when the cell had no idle reading.</summary>
    public double? IdleLevel { get; set; }

    /// <summary>The idle level less the first plateau's, divided by the tenants added since: what is still held per tenant when nothing uses it.</summary>
    public double? RetainedPerTenant { get; set; }

    /// <summary>The smallest value the metric took anywhere, so a series that never moved is visible as such.</summary>
    public double Minimum { get; set; }

    /// <summary>The largest value the metric took anywhere.</summary>
    public double Maximum { get; set; }

    /// <summary>How many raw points the metric had.</summary>
    public int RawPoints { get; set; }
}

/// <summary>The whole footprint analysis of one cell.</summary>
public sealed class FootprintAnalysisResult
{
    /// <summary>The cell.</summary>
    public string CellId { get; set; } = "";

    /// <summary>The thresholds used.</summary>
    public FootprintAnalysisOptions Options { get; set; } = new();

    /// <summary>The ramp as it ran.</summary>
    public RampRecord Ramp { get; set; } = new();

    /// <summary>How many samples there were.</summary>
    public int Samples { get; set; }

    /// <summary>How many samples lacked a source.</summary>
    public int Gaps { get; set; }

    /// <summary>The worst verdict of any metric.</summary>
    public string Verdict { get; set; } = FootprintVerdict.Inconclusive;

    /// <summary>One entry per metric.</summary>
    public List<MetricAnalysis> Metrics { get; set; } = [];
}

/// <summary>Judges whether a process's footprint grows with time, with runs, or faster than with its tenants.</summary>
/// <remarks>
/// <para>
/// Two questions, asked separately because they have different answers. <em>Inside a plateau</em> the tenants are
/// fixed, so any sustained rise is state that follows time or runs: a leak by every reading of the invariant.
/// <em>Between plateaus</em> the tenants changed, so a rise is the cost of those tenants; a rise in bytes is
/// accepted if the cost per tenant is steady, and is a finding if it climbs from one step to the next. A rise in a
/// count of threads, timers, descriptors or connections is a finding at any size: none of them should follow the
/// number of tenants.
/// </para>
/// <para>
/// 🚨 A flat verdict is only as good as the smallest growth the analysis could have seen, so every plateau carries
/// its <see cref="StageTrend.Floor"/>. A short plateau with a high floor is "no leak above that", not "no leak".
/// </para>
/// </remarks>
public static class FootprintAnalysis
{
    /// <summary>Analyses a cell's footprint samples against its ramp.</summary>
    /// <param name="cellId">The cell.</param>
    /// <param name="samples">Every sample, in time order.</param>
    /// <param name="ramp">The ramp as it ran.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The analysis.</returns>
    public static FootprintAnalysisResult Analyse(
        string cellId,
        IReadOnlyList<FootprintSample> samples,
        RampRecord ramp,
        FootprintAnalysisOptions options)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(ramp);
        ArgumentNullException.ThrowIfNull(options);

        var result = new FootprintAnalysisResult
        {
            CellId = cellId,
            Options = options,
            Ramp = ramp,
            Samples = samples.Count,
            Gaps = samples.Count(static s => s.Missing is { Count: > 0 }),
        };

        var series = Extract(samples);
        var holds = ramp.Marks.Where(static m => string.Equals(m.Phase, RampPhase.Hold, StringComparison.Ordinal)).OrderBy(static m => m.Stage).ToList();
        var idle = ramp.Marks.FirstOrDefault(static m => string.Equals(m.Phase, RampPhase.Idle, StringComparison.Ordinal));

        foreach (var (name, points) in series.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (FootprintCatalog.Find(name) is not { } metric)
            {
                continue;
            }

            result.Metrics.Add(AnalyseMetric(metric, points, holds, idle, ramp, samples, options));
        }

        result.Verdict = result.Metrics.Any(static m => string.Equals(m.Verdict, FootprintVerdict.Red, StringComparison.Ordinal))
            ? FootprintVerdict.Red
            : result.Metrics.Any(static m => string.Equals(m.Verdict, FootprintVerdict.Green, StringComparison.Ordinal))
                ? FootprintVerdict.Green
                : FootprintVerdict.Inconclusive;

        return result;
    }

    /// <summary>Turns samples into one time series per reading.</summary>
    /// <param name="samples">The samples.</param>
    /// <returns>The series by reading name.</returns>
    public static Dictionary<string, List<(double Time, double Value)>> Extract(IReadOnlyList<FootprintSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var series = new Dictionary<string, List<(double Time, double Value)>>(StringComparer.Ordinal);

        foreach (var sample in samples)
        {
            foreach (var (name, value) in sample.Values)
            {
                if (!double.IsFinite(value))
                {
                    continue;
                }

                if (!series.TryGetValue(name, out var list))
                {
                    list = [];
                    series[name] = list;
                }

                list.Add((sample.OffsetSeconds, value));
            }

            if (sample.InstrumentSeries is { } instruments)
            {
                foreach (var (instrument, count) in instruments)
                {
                    var name = FootprintCatalog.SeriesPrefix + instrument;

                    if (!series.TryGetValue(name, out var list))
                    {
                        list = [];
                        series[name] = list;
                    }

                    list.Add((sample.OffsetSeconds, count));
                }
            }
        }

        return series;
    }

    private static MetricAnalysis AnalyseMetric(
        FootprintMetric metric,
        List<(double Time, double Value)> points,
        List<StageMark> holds,
        StageMark? idle,
        RampRecord ramp,
        IReadOnlyList<FootprintSample> samples,
        FootprintAnalysisOptions options)
    {
        var analysis = new MetricAnalysis
        {
            Name = metric.Name,
            Label = metric.Label,
            Kind = metric.Kind,
            RawPoints = points.Count,
            Minimum = points.Count == 0 ? double.NaN : points.Min(static p => p.Value),
            Maximum = points.Count == 0 ? double.NaN : points.Max(static p => p.Value),
        };

        var conclusive = 0;

        if (metric.Kind == MetricKind.Pooled && holds.Count > 0)
        {
            // How far the pool moves with nothing about the tenants changing is read once, from the first plateau, and
            // is the smallest rise that counts everywhere after it: inside a plateau and from one plateau to the next.
            var first = Window(holds[0], options);

            analysis.Breath = Math.Max(
                options.PooledBreath,
                Spread(points.Where(p => p.Time >= first.Start && p.Time <= first.End).ToList()));
        }

        foreach (var hold in holds)
        {
            var window = Window(hold, options);
            var inWindow = points.Where(p => p.Time >= window.Start && p.Time <= window.End).ToList();

            var trend = Trend(hold, window, inWindow, metric.Kind, options, RequestsPerSecond(hold), analysis.Breath ?? 0);
            analysis.Trends.Add(trend);

            if (metric.Kind == MetricKind.Pooled)
            {
                // A pool moves with the load and sits on one level for a while: the last third of a plateau is a stretch
                // of that, not the plateau, so the plateau as a whole stands for it.
                analysis.Levels.Add(WholeWindowLevel(inWindow));
                analysis.Troughs.Add(Trough(inWindow, window.Start, options.BucketSeconds));
            }
            else
            {
                analysis.Levels.Add(Level(inWindow));
            }

            if (!string.Equals(trend.Verdict, FootprintVerdict.Inconclusive, StringComparison.Ordinal))
            {
                conclusive++;
            }
        }

        ConfirmDrift(analysis);

        var exhausted = metric.Kind == MetricKind.Pooled && ramp.MaxPoolSize is > 0 ? ExhaustedPlateaus(analysis, holds, ramp.MaxPoolSize.Value) : [];
        analysis.Reasons.AddRange(exhausted);

        foreach (var trend in analysis.Trends)
        {
            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"stage {trend.Stage} ({trend.Tenants} tenants): grew {Format(metric.Kind, trend.Growth)}{PerRequest(metric.Kind, trend)} inside a plateau with the tenants fixed (tau {trend.Tau:0.00}, p {trend.PValue:0.0000}, floor {Format(metric.Kind, trend.Floor)})");

            if (string.Equals(trend.Verdict, FootprintVerdict.Red, StringComparison.Ordinal))
            {
                analysis.Reasons.Add(line);
            }
            else if (string.Equals(trend.Verdict, FootprintVerdict.Transient, StringComparison.Ordinal))
            {
                analysis.Notes.Add(line + "; it did not keep rising in the next plateau, so it is set aside as a warm-up");
            }
            else if (string.Equals(trend.Verdict, FootprintVerdict.Jump, StringComparison.Ordinal))
            {
                analysis.Notes.Add(line + "; the rise is one jump, level before and after, not a slope in both halves of the plateau, so it is set aside");
            }
        }

        for (var index = 1; index < holds.Count; index++)
        {
            analysis.Steps.Add(Step(analysis, holds, index, metric.Kind, options));
        }

        ConfirmSteps(analysis);

        foreach (var step in analysis.Steps)
        {
            if (!string.Equals(step.Verdict, FootprintVerdict.Inconclusive, StringComparison.Ordinal))
            {
                conclusive++;
            }

            if (string.Equals(step.Verdict, FootprintVerdict.Red, StringComparison.Ordinal))
            {
                analysis.Reasons.Add($"stage {step.ToStage}: {step.Note}");
            }
            else if (string.Equals(step.Verdict, FootprintVerdict.Transient, StringComparison.Ordinal))
            {
                analysis.Notes.Add($"stage {step.ToStage}: {step.Note}");
            }
        }

        if (idle is not null && holds.Count > 0)
        {
            var idlePoints = points.Where(p => p.Time >= idle.StartSeconds && p.Time <= idle.EndSeconds + 1).ToList();

            // The closing reading is collected, so a precise metric has a value here; for the rest the last third will do.
            var level = idlePoints.Count == 0 ? (double?)null : Level(idlePoints);

            if (level is { } settled)
            {
                analysis.IdleLevel = settled;

                if (analysis.Levels.Count > 0 && analysis.Levels[0] is { } first && ramp.Marks.Count > 0)
                {
                    var added = holds[^1].Tenants - holds[0].Tenants;

                    if (added > 0)
                    {
                        analysis.RetainedPerTenant = (settled - first) / added;
                    }
                }
            }
        }

        var red = analysis.Trends.Any(static t => string.Equals(t.Verdict, FootprintVerdict.Red, StringComparison.Ordinal))
            || analysis.Steps.Any(static s => string.Equals(s.Verdict, FootprintVerdict.Red, StringComparison.Ordinal))
            || exhausted.Count > 0;

        // Green is a claim about every plateau. A metric with one plateau too thin to judge is inconclusive, not
        // green: the plateau nobody could judge is exactly where a leak would be.
        var everyPlateauJudged = analysis.Trends.Count > 0
            && analysis.Trends.All(static t => !string.Equals(t.Verdict, FootprintVerdict.Inconclusive, StringComparison.Ordinal));

        analysis.Verdict = red
            ? FootprintVerdict.Red
            : everyPlateauJudged && conclusive > 0 ? FootprintVerdict.Green : FootprintVerdict.Inconclusive;

        return analysis;
    }

    // A leak follows time or runs, and time and runs go on into the next plateau. A rise that is there in one plateau
    // and gone in the next, at the same load, did not follow either: it was the process settling. A plateau that is
    // last, or whose successor could not be judged, cannot be set aside this way and stays red.
    private static void ConfirmDrift(MetricAnalysis analysis)
    {
        var drifted = analysis.Trends
            .Select(static t => string.Equals(t.Verdict, FootprintVerdict.Red, StringComparison.Ordinal))
            .ToList();

        for (var index = 0; index < analysis.Trends.Count - 1; index++)
        {
            var next = analysis.Trends[index + 1];

            if (drifted[index] && !drifted[index + 1]
                && !string.Equals(next.Verdict, FootprintVerdict.Inconclusive, StringComparison.Ordinal))
            {
                analysis.Trends[index].Verdict = FootprintVerdict.Transient;
            }
        }
    }

    // The same argument for the steps. A count that rose between two plateaus because tenants joined must rise again,
    // and by more, when ten times as many join. If a later step that added at least as many tenants shows no rise, the
    // early rise was the process warming up (a thread pool finding its size, a pool opening its connections).
    private static void ConfirmSteps(MetricAnalysis analysis)
    {
        if (analysis.Kind is not (MetricKind.Count or MetricKind.Pooled))
        {
            return;
        }

        for (var index = 0; index < analysis.Steps.Count; index++)
        {
            var step = analysis.Steps[index];

            if (!string.Equals(step.Verdict, FootprintVerdict.Red, StringComparison.Ordinal))
            {
                continue;
            }

            var refuting = analysis.Steps.Skip(index + 1).FirstOrDefault(later =>
                later.AddedTenants >= step.AddedTenants
                && string.Equals(later.Verdict, FootprintVerdict.Green, StringComparison.Ordinal)
                && later.Delta < later.Floor);

            if (refuting is not null)
            {
                step.Verdict = FootprintVerdict.Transient;
                step.Note += string.Create(
                    CultureInfo.InvariantCulture,
                    $"; the step to stage {refuting.ToStage} added {refuting.AddedTenants} tenants and showed no rise, so this is set aside as a warm-up, not a resource per tenant");
            }
        }
    }

    private static string PerRequest(MetricKind kind, StageTrend trend)
        => kind != MetricKind.Count && trend.GrowthPerRequest is { } perRequest
            ? string.Create(CultureInfo.InvariantCulture, $" ({Format(kind, perRequest)} per request)")
            : "";

    private static (double Start, double End) Window(StageMark hold, FootprintAnalysisOptions options)
    {
        var length = Math.Max(0, hold.EndSeconds - hold.StartSeconds);
        var settle = Math.Min(options.SettleCapSeconds, options.SettleFraction * length);

        return (hold.StartSeconds + settle, hold.EndSeconds);
    }

    private static double RequestsPerSecond(StageMark hold)
    {
        var length = hold.EndSeconds - hold.StartSeconds;

        return length > 0 ? (hold.IssuedAtEnd - hold.IssuedAtStart) / length : 0;
    }

    private static StageTrend Trend(
        StageMark hold,
        (double Start, double End) window,
        List<(double Time, double Value)> inWindow,
        MetricKind kind,
        FootprintAnalysisOptions options,
        double requestsPerSecond,
        double minimumFloor)
    {
        var trend = new StageTrend
        {
            Stage = hold.Stage,
            Tenants = hold.Tenants,
            WindowStartSeconds = Math.Round(window.Start, 3),
            WindowEndSeconds = Math.Round(window.End, 3),
            RequestsPerSecond = Math.Round(requestsPerSecond, 3),
        };

        var buckets = Bucket(inWindow, window.Start, options.BucketSeconds);
        trend.Points = buckets.Count;

        if (buckets.Count < options.MinimumPoints)
        {
            trend.Floor = Math.Max(options.Floor(kind, buckets.Count == 0 ? 0 : buckets[0].Value), minimumFloor);
            return trend;
        }

        var times = buckets.Select(static b => b.Time).ToList();
        var values = buckets.Select(static b => b.Value).ToList();
        var third = Math.Max(1, buckets.Count / 3);

        trend.FirstThird = TrendStatistics.Median(values.Take(third).ToList());
        trend.LastThird = TrendStatistics.Median(values.Skip(values.Count - third).ToList());
        trend.SlopePerSecond = TrendStatistics.TheilSen(times, values);
        trend.Growth = trend.SlopePerSecond * (times[^1] - times[0]);

        var requestsInWindow = requestsPerSecond * (times[^1] - times[0]);
        trend.GrowthPerRequest = requestsInWindow > 0 ? trend.Growth / requestsInWindow : null;

        var test = TrendStatistics.MannKendall(values);
        trend.Tau = test.Tau;
        trend.PValue = test.PValue;
        trend.Floor = Math.Max(options.Floor(kind, trend.FirstThird), minimumFloor);

        // All of the following, so that no one of them can carry a verdict alone: a significant rank trend that
        // is tiny is breathing; a big difference between thirds without a trend is one burst; a slope without
        // either is an outlier pulling a fit.
        var drifts = test.PValue < options.Alpha
            && test.Tau > 0
            && trend.Growth >= trend.Floor
            && trend.LastThird - trend.FirstThird >= trend.Floor / 2;

        // A slope is there in both halves of the window; a single jump is in one of them. Each half must carry at
        // least a quarter of the floor on its own, or the rise is one step that the rank test, which counts a step
        // as a trend, cannot tell from a leak.
        var gradual = drifts && HalfGrowth(times, values, 0, values.Count / 2) >= trend.Floor / 4
            && HalfGrowth(times, values, values.Count / 2, values.Count) >= trend.Floor / 4;

        trend.Verdict = !drifts
            ? FootprintVerdict.Green
            : gradual ? FootprintVerdict.Red : FootprintVerdict.Jump;
        return trend;
    }

    private static double HalfGrowth(List<double> times, List<double> values, int from, int to)
    {
        var x = times.Skip(from).Take(to - from).ToList();
        var y = values.Skip(from).Take(to - from).ToList();

        return x.Count < 2 ? 0 : TrendStatistics.TheilSen(x, y) * (x[^1] - x[0]);
    }

    private static List<(double Time, double Value)> Bucket(
        List<(double Time, double Value)> points,
        double origin,
        double width)
    {
        var buckets = new SortedDictionary<long, List<(double Time, double Value)>>();

        foreach (var point in points)
        {
            var key = (long)Math.Floor((point.Time - origin) / width);

            if (!buckets.TryGetValue(key, out var list))
            {
                list = [];
                buckets[key] = list;
            }

            list.Add(point);
        }

        return buckets.Values
            .Select(static list => (
                Time: list.Average(static p => p.Time),
                Value: TrendStatistics.Median(list.Select(static p => p.Value).ToList())))
            .ToList();
    }

    // The level of a plateau: the median of the last third of its points, or its last point when it has few.
    private static double? Level(List<(double Time, double Value)> inWindow)
    {
        if (inWindow.Count == 0)
        {
            return null;
        }

        var ordered = inWindow.OrderBy(static p => p.Time).ToList();
        var take = Math.Max(1, ordered.Count / 3);

        return TrendStatistics.Median(ordered.Skip(ordered.Count - take).Select(static p => p.Value).ToList());
    }

    // The level of a plateau for a pooled count: the median of every point in its window.
    private static double? WholeWindowLevel(List<(double Time, double Value)> inWindow)
        => inWindow.Count == 0 ? null : TrendStatistics.Median(inWindow.Select(static p => p.Value).ToList());

    // The lowest window of a plateau: the window medians are the smoothed series the trend is judged on.
    private static double? Trough(List<(double Time, double Value)> inWindow, double origin, double width)
    {
        var buckets = Bucket(inWindow, origin, width);

        return buckets.Count == 0 ? null : buckets.Min(static b => b.Value);
    }

    // How far a plateau's readings range: the 95th percentile less the 5th, so one stray reading is not the range.
    private static double Spread(List<(double Time, double Value)> inWindow)
    {
        if (inWindow.Count < 2)
        {
            return 0;
        }

        var ordered = inWindow.Select(static p => p.Value).Order().ToList();

        return Percentile(ordered, 0.95) - Percentile(ordered, 0.05);
    }

    private static double Percentile(List<double> ordered, double fraction)
    {
        var position = fraction * (ordered.Count - 1);
        var below = (int)Math.Floor(position);
        var above = Math.Min(below + 1, ordered.Count - 1);

        return ordered[below] + ((ordered[above] - ordered[below]) * (position - below));
    }

    // A pool whose plateau sits at its ceiling has used itself up: tenants are waiting for a connection, whatever the
    // plateau before it looked like.
    private static List<string> ExhaustedPlateaus(MetricAnalysis analysis, List<StageMark> holds, int ceiling)
    {
        var reasons = new List<string>();

        for (var index = 0; index < holds.Count && index < analysis.Levels.Count; index++)
        {
            if (analysis.Levels[index] is { } level && level >= ceiling)
            {
                reasons.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"stage {holds[index].Stage} ({holds[index].Tenants} tenants): the median of the plateau is {level:0.##}, the pool's limit is {ceiling}; the pool is used up"));
            }
        }

        return reasons;
    }

    private static StageStep Step(
        MetricAnalysis analysis,
        List<StageMark> holds,
        int index,
        MetricKind kind,
        FootprintAnalysisOptions options)
    {
        var step = new StageStep
        {
            ToStage = holds[index].Stage,
            AddedTenants = holds[index].Tenants - holds[index - 1].Tenants,
        };

        if (analysis.Levels[index - 1] is not { } from || analysis.Levels[index] is not { } to)
        {
            step.Note = "a plateau had no reading";
            return step;
        }

        step.From = from;
        step.To = to;
        step.Delta = to - from;
        step.PerTenant = step.AddedTenants > 0 ? step.Delta / step.AddedTenants : 0;
        step.Floor = options.Floor(kind, from);

        if (kind == MetricKind.Pooled)
        {
            // A pool moves by itself between plateaus: a rise smaller than that is the pool, not the tenants.
            step.Floor = Math.Max(step.Floor, analysis.Breath ?? 0);
        }

        if (step.Delta < step.Floor)
        {
            step.Verdict = FootprintVerdict.Green;
            step.Note = kind == MetricKind.Pooled
                ? string.Create(CultureInfo.InvariantCulture, $"{from:0.##} -> {to:0.##}: within the {step.Floor:0.##} the pool moves between plateaus by itself")
                : "no change above the floor";
            return step;
        }

        if (kind == MetricKind.Pooled
            && analysis.Troughs[index - 1] is { } troughBefore
            && analysis.Troughs[index] is { } troughAfter
            && troughAfter <= troughBefore)
        {
            // Connections held for good do not go back down when the load eases; a pool that filled for the load does.
            step.Verdict = FootprintVerdict.Green;
            step.Note = string.Create(
                CultureInfo.InvariantCulture,
                $"{from:0.##} -> {to:0.##}, but the lowest window did not rise ({troughBefore:0.##} -> {troughAfter:0.##}): the pool filled for the load, no connection is held per tenant");
            return step;
        }

        if (kind is MetricKind.Count or MetricKind.Pooled)
        {
            // Not a byte cost a tenant may reasonably have: a thread, a timer, a descriptor or a connection per tenant
            // is a resource that was started for a caller and not given back.
            step.Verdict = FootprintVerdict.Red;
            step.Note = string.Create(
                CultureInfo.InvariantCulture,
                $"{from:0.##} -> {to:0.##} across {step.AddedTenants} added tenants ({step.PerTenant:0.####} per tenant); a count of held resources should not follow the number of tenants{(kind == MetricKind.Pooled ? ", and this is more than the pool moves by itself and its lowest window rose too" : "")}");
            return step;
        }

        step.Verdict = FootprintVerdict.Green;
        step.Note = string.Create(
            CultureInfo.InvariantCulture,
            $"{Format(kind, step.Delta)} across {step.AddedTenants} added tenants = {Format(kind, step.PerTenant)} per tenant");

        var previous = analysis.Steps.Count > 0 ? analysis.Steps[^1] : null;

        if (previous is not null && previous.Delta >= previous.Floor && previous.PerTenant > 0)
        {
            var ratio = step.PerTenant / previous.PerTenant;
            step.RatioToPrevious = ratio;

            if (ratio > options.SuperLinearRatio)
            {
                step.Verdict = FootprintVerdict.Red;
                step.Note = string.Create(
                    CultureInfo.InvariantCulture,
                    $"cost per tenant rose {ratio:0.0}x over the step before ({Format(kind, previous.PerTenant)} -> {Format(kind, step.PerTenant)}); super-linear in tenants");
            }
        }

        return step;
    }

    /// <summary>Writes a value in the unit its kind implies.</summary>
    /// <param name="kind">The metric's kind.</param>
    /// <param name="value">The value.</param>
    /// <returns>For bytes, a binary-prefixed size; for counts, a plain number.</returns>
    public static string Format(MetricKind kind, double value)
    {
        if (kind is MetricKind.Count or MetricKind.Objects or MetricKind.Pooled)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        var magnitude = Math.Abs(value);
        var sign = value < 0 ? "-" : "";

        return magnitude switch
        {
            >= 1024d * 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{sign}{magnitude / (1024d * 1024 * 1024):0.##} GiB"),
            >= 1024d * 1024 => string.Create(CultureInfo.InvariantCulture, $"{sign}{magnitude / (1024d * 1024):0.##} MiB"),
            >= 1024d => string.Create(CultureInfo.InvariantCulture, $"{sign}{magnitude / 1024d:0.##} KiB"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{sign}{magnitude:0.##} B"),
        };
    }
}
