using Tracon.CapacityDriver;

namespace Tracon.Capacity.Tests;

/// <summary>The percentile and cohort arithmetic, against hand-computed data.</summary>
/// <remarks>
/// Every expectation here was computed by hand from a set small enough to
/// check by eye. A percentile implementation validated against another
/// implementation proves only that the two agree.
/// </remarks>
public sealed class CapacityStatisticsTests
{
    [Fact]
    public void Nearest_rank_picks_the_sample_at_the_ceiling_of_the_rank()
    {
        // Ten samples 10..100. p50 -> ceil(0.50*10)=5 -> the 5th smallest = 50.
        // p95 -> ceil(9.5)=10 -> 100. p99 -> ceil(9.9)=10 -> 100.
        var statistics = LatencyStatistics.From([100, 10, 20, 90, 30, 80, 40, 70, 50, 60]);

        statistics.Count.ShouldBe(10);
        statistics.P50.ShouldBe(50);
        statistics.P95.ShouldBe(100);
        statistics.P99.ShouldBe(100);
        statistics.Minimum.ShouldBe(10);
        statistics.Maximum.ShouldBe(100);
    }

    [Fact]
    public void A_single_sample_is_every_percentile()
    {
        var statistics = LatencyStatistics.From([42]);

        statistics.P50.ShouldBe(42);
        statistics.P95.ShouldBe(42);
        statistics.P99.ShouldBe(42);
    }

    [Fact]
    public void An_empty_set_has_no_percentile_rather_than_a_zero()
    {
        var statistics = LatencyStatistics.From([]);

        statistics.Count.ShouldBe(0);
        statistics.P50.ShouldBeNull();
        statistics.Mean.ShouldBeNull();
        statistics.Maximum.ShouldBeNull();

        // 🚨 A zero would read as "measured, and it was instant".
        statistics.ToSummary().P95.ShouldBeNull();
    }

    [Fact]
    public void Low_sample_flags_follow_the_published_floors()
    {
        LatencyStatistics.From(Enumerable.Repeat(1d, 99)).LowSampleP95.ShouldBeTrue();
        LatencyStatistics.From(Enumerable.Repeat(1d, 100)).LowSampleP95.ShouldBeFalse();
        LatencyStatistics.From(Enumerable.Repeat(1d, 999)).LowSampleP99.ShouldBeTrue();
        LatencyStatistics.From(Enumerable.Repeat(1d, 1000)).LowSampleP99.ShouldBeFalse();
    }

    [Fact]
    public void An_empty_set_carries_no_low_sample_warning_because_it_carries_no_percentile()
    {
        var statistics = LatencyStatistics.From([]);

        statistics.LowSampleP95.ShouldBeFalse();
        statistics.LowSampleP99.ShouldBeFalse();
    }

    [Fact]
    public void Repeats_are_merged_at_the_sample_level_not_by_averaging_percentiles()
    {
        // Averaging the two p50 values would give (2+200)/2 = 101. Merging the
        // samples gives the 3rd of [1,2,3,100,200,300] = 3. The difference is
        // the whole reason Merge exists.
        var first = LatencyStatistics.From([1, 2, 3]);
        var second = LatencyStatistics.From([100, 200, 300]);

        LatencyStatistics.Merge(first, second).P50.ShouldBe(3);
    }

    [Fact]
    public void A_percentile_outside_the_open_interval_is_refused()
    {
        var statistics = LatencyStatistics.From([1, 2, 3]);

        Should.Throw<ArgumentOutOfRangeException>(() => statistics.Percentile(0));
        Should.Throw<ArgumentOutOfRangeException>(() => statistics.Percentile(101));
    }

    [Fact]
    public void The_summary_names_the_method_so_a_reader_never_has_to_guess()
        => LatencyStatistics.From([1]).ToSummary().Method.ShouldBe("nearest-rank");
}
