namespace Tracon.CapacityDriver;

/// <summary>The outcome of a Mann-Kendall trend test.</summary>
/// <param name="Count">How many points the test saw.</param>
/// <param name="S">The sum of the signs of every later-minus-earlier pair.</param>
/// <param name="Tau">S divided by the number of pairs: +1 for a series that only rises, -1 for one that only falls.</param>
/// <param name="Z">The normal score of S, with the tie correction and the continuity correction.</param>
/// <param name="PValue">The two-sided probability of a score this extreme in a series with no trend.</param>
public readonly record struct TrendTest(int Count, long S, double Tau, double Z, double PValue);

/// <summary>The nonparametric trend statistics the footprint analysis rests on.</summary>
/// <remarks>
/// <para>
/// 🚨 Why these two and not a least-squares fit. A process's memory is a sawtooth, not a line: the collector frees
/// in bursts and the working set follows with a delay. A least-squares slope is dragged by one burst and by the
/// shape of the window; the Theil-Sen slope is the median of every pairwise slope and moves little for up to about
/// a quarter of the points being outliers. The Mann-Kendall test asks only "do later points tend to be higher than
/// earlier ones", needs no distribution, and has an exact meaning for a monotone leak: tau near 1.
/// </para>
/// <para>
/// The test assumes independent points. Window medians of a process counter are positively autocorrelated, which
/// makes the p-value too small. The analysis therefore never judges on p alone: it also asks that the growth be
/// larger than a floor and that the last third of the window sit above the first. A p-value that survives only on
/// autocorrelation cannot pass those.
/// </para>
/// </remarks>
public static class TrendStatistics
{
    /// <summary>The median of a set of values.</summary>
    /// <param name="values">The values; not modified.</param>
    /// <returns>The median, or <see cref="double.NaN"/> for an empty set.</returns>
    public static double Median(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
        {
            return double.NaN;
        }

        var sorted = values.OrderBy(static v => v).ToArray();
        var middle = sorted.Length / 2;

        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>The Mann-Kendall test on a series in time order.</summary>
    /// <param name="values">The series.</param>
    /// <returns>The test; with fewer than three points the p-value is 1.</returns>
    public static TrendTest MannKendall(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var n = values.Count;

        if (n < 3)
        {
            return new TrendTest(n, 0, 0, 0, 1);
        }

        long s = 0;

        for (var i = 0; i < n - 1; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                s += Math.Sign(values[j] - values[i]);
            }
        }

        // Ties shrink the variance: a flat stretch carries less evidence than the same count of distinct values.
        var variance = n * (n - 1.0) * ((2.0 * n) + 5);

        foreach (var group in values.GroupBy(static v => v))
        {
            var t = group.Count();

            if (t > 1)
            {
                variance -= t * (t - 1.0) * ((2.0 * t) + 5);
            }
        }

        variance /= 18;

        double z = 0;

        if (variance > 0)
        {
            z = s switch
            {
                > 0 => (s - 1) / Math.Sqrt(variance),
                < 0 => (s + 1) / Math.Sqrt(variance),
                _ => 0,
            };
        }

        var pairs = n * (n - 1L) / 2;

        return new TrendTest(n, s, s / (double)pairs, z, Math.Clamp(Erfc(Math.Abs(z) / Math.Sqrt(2)), 0, 1));
    }

    /// <summary>The Theil-Sen slope: the median of the slope between every pair of points.</summary>
    /// <param name="x">The times, strictly increasing between pairs that count.</param>
    /// <param name="y">The values.</param>
    /// <returns>The slope in units of <paramref name="y"/> per unit of <paramref name="x"/>; zero with fewer than two points.</returns>
    public static double TheilSen(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);

        if (x.Count != y.Count)
        {
            throw new ArgumentException("x and y must have the same length.", nameof(y));
        }

        var slopes = new List<double>(Math.Max(0, x.Count * (x.Count - 1) / 2));

        for (var i = 0; i < x.Count - 1; i++)
        {
            for (var j = i + 1; j < x.Count; j++)
            {
                var dx = x[j] - x[i];

                if (dx > 0)
                {
                    slopes.Add((y[j] - y[i]) / dx);
                }
            }
        }

        return slopes.Count == 0 ? 0 : Median(slopes);
    }

    // Complementary error function, Numerical Recipes "erfcc": fractional error below 1.2e-7 everywhere, which is
    // far finer than a decision at p = 0.01 needs.
    private static double Erfc(double x)
    {
        var z = Math.Abs(x);
        var t = 1 / (1 + (0.5 * z));

        var polynomial = -z * z - 1.26551223
            + (t * (1.00002368
            + (t * (0.37409196
            + (t * (0.09678418
            + (t * (-0.18628806
            + (t * (0.27886807
            + (t * (-1.13520398
            + (t * (1.48851587
            + (t * (-0.82215223
            + (t * 0.17087277)))))))))))))))));

        var result = t * Math.Exp(polynomial);

        return x >= 0 ? result : 2 - result;
    }
}
