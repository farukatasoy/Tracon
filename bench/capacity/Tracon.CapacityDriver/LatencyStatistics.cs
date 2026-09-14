using System.Globalization;

namespace Tracon.CapacityDriver;

/// <summary>A latency distribution summarised from measured samples.</summary>
/// <remarks>
/// <para>
/// 🚨 Percentiles are computed with the <strong>nearest-rank</strong> method
/// over the retained samples, and the sample count travels with every
/// percentile. A p99 taken from 40 samples is a number, not an estimate, and
/// reporting it without <see cref="LowSampleP99"/> beside it is how a capacity
/// report starts lying.
/// </para>
/// <para>
/// 🚨 Repeats are never averaged percentile-by-percentile. Merging two cells
/// means merging their <em>samples</em> (<see cref="Merge"/>) and computing the
/// percentile once; averaging p95 values is an arithmetic operation with no
/// distributional meaning.
/// </para>
/// </remarks>
public sealed class LatencyStatistics
{
    /// <summary>Below this many samples a p95 carries a low-sample warning.</summary>
    public const int P95SampleFloor = 100;

    /// <summary>Below this many samples a p99 carries a low-sample warning.</summary>
    public const int P99SampleFloor = 1000;

    private readonly List<double> _samples;

    private LatencyStatistics(List<double> samples) => _samples = samples;

    /// <summary>Builds a distribution from measured values, in any order.</summary>
    /// <param name="values">The measured values, in milliseconds.</param>
    /// <returns>The distribution.</returns>
    public static LatencyStatistics From(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var samples = values.ToList();
        samples.Sort();
        return new LatencyStatistics(samples);
    }

    /// <summary>Combines two distributions by combining their samples.</summary>
    /// <param name="left">One distribution.</param>
    /// <param name="right">The other.</param>
    /// <returns>The combined distribution.</returns>
    public static LatencyStatistics Merge(LatencyStatistics left, LatencyStatistics right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var samples = new List<double>(left._samples.Count + right._samples.Count);
        samples.AddRange(left._samples);
        samples.AddRange(right._samples);
        samples.Sort();
        return new LatencyStatistics(samples);
    }

    /// <summary>How many samples the distribution holds.</summary>
    public int Count => _samples.Count;

    /// <summary>The smallest sample, or <see langword="null"/> when there are none.</summary>
    public double? Minimum => _samples.Count == 0 ? null : _samples[0];

    /// <summary>The largest sample, or <see langword="null"/> when there are none.</summary>
    public double? Maximum => _samples.Count == 0 ? null : _samples[^1];

    /// <summary>The arithmetic mean, or <see langword="null"/> when there are no samples.</summary>
    public double? Mean => _samples.Count == 0 ? null : _samples.Sum() / _samples.Count;

    /// <summary>The median.</summary>
    public double? P50 => Percentile(50);

    /// <summary>The 95th percentile.</summary>
    public double? P95 => Percentile(95);

    /// <summary>The 99th percentile.</summary>
    public double? P99 => Percentile(99);

    /// <summary>Whether <see cref="P95"/> came from fewer than <see cref="P95SampleFloor"/> samples.</summary>
    public bool LowSampleP95 => _samples.Count > 0 && _samples.Count < P95SampleFloor;

    /// <summary>Whether <see cref="P99"/> came from fewer than <see cref="P99SampleFloor"/> samples.</summary>
    public bool LowSampleP99 => _samples.Count > 0 && _samples.Count < P99SampleFloor;

    /// <summary>The nearest-rank percentile of the retained samples.</summary>
    /// <param name="percentile">A value in the open interval (0, 100].</param>
    /// <returns>The value at that rank, or <see langword="null"/> when there are no samples.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The percentile is outside (0, 100].</exception>
    public double? Percentile(double percentile)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(percentile, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percentile, 100);

        if (_samples.Count == 0)
        {
            return null;
        }

        // Nearest rank: the smallest index whose share of the sorted samples is
        // at least the requested percentile. Ceiling, then clamp - the clamp
        // only matters for exactly 100.
        var rank = (int)Math.Ceiling(percentile / 100d * _samples.Count);
        var index = Math.Clamp(rank - 1, 0, _samples.Count - 1);
        return _samples[index];
    }

    /// <summary>The distribution as the shape the report writes.</summary>
    /// <returns>The summary.</returns>
    public LatencySummary ToSummary() => new()
    {
        Count = Count,
        Minimum = Round(Minimum),
        Mean = Round(Mean),
        P50 = Round(P50),
        P95 = Round(P95),
        P99 = Round(P99),
        Maximum = Round(Maximum),
        LowSampleP95 = LowSampleP95,
        LowSampleP99 = LowSampleP99,
    };

    private static double? Round(double? value)
        => value is null ? null : Math.Round(value.Value, 3, MidpointRounding.AwayFromZero);

    /// <summary>A short, culture-invariant rendering for a report table cell.</summary>
    /// <param name="value">The value in milliseconds.</param>
    /// <returns>The rendering, or <c>—</c> when there is no value.</returns>
    public static string Format(double? value)
        => value is null ? "—" : value.Value.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>A latency distribution as it is written to disk.</summary>
public sealed class LatencySummary
{
    /// <summary>How many samples it was computed from.</summary>
    public int Count { get; set; }

    /// <summary>The smallest sample, in milliseconds.</summary>
    public double? Minimum { get; set; }

    /// <summary>The arithmetic mean, in milliseconds.</summary>
    public double? Mean { get; set; }

    /// <summary>The median, in milliseconds.</summary>
    public double? P50 { get; set; }

    /// <summary>The 95th percentile, in milliseconds.</summary>
    public double? P95 { get; set; }

    /// <summary>The 99th percentile, in milliseconds.</summary>
    public double? P99 { get; set; }

    /// <summary>The largest sample, in milliseconds.</summary>
    public double? Maximum { get; set; }

    /// <summary>Whether the 95th percentile came from too few samples to be trusted.</summary>
    public bool LowSampleP95 { get; set; }

    /// <summary>Whether the 99th percentile came from too few samples to be trusted.</summary>
    public bool LowSampleP99 { get; set; }

    /// <summary>The method these percentiles were computed with, written so a reader never has to guess.</summary>
    public string Method { get; set; } = "nearest-rank";

    /// <summary>
    /// When repeats were merged, the sample count of the THINNEST repeat.
    /// </summary>
    /// <remarks>
    /// 🚨 A merged count can clear the floor while no single window did: three
    /// 58-sample repeats make 174. The merged percentile is still the honest
    /// one, but a reader deciding how much to trust it needs to know that no
    /// individual window supported it.
    /// </remarks>
    public int? ThinnestRepeat { get; set; }
}
