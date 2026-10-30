namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// <see cref="RunUsageSum.Add"/> is the one place where the run recorder, the cost combiner and the
/// webhook path add two <see cref="RunUsage"/> records. A counter it does not carry is
/// dropped from every run that has a side-channel call: the written tokens would be priced as plain
/// input, and the quota, the webhook total and the statistics would fall short.
/// </summary>
public sealed class RunUsageSumTests
{
    [Fact]
    public void Both_sides_cache_write_counters_are_summed_for_each_time_to_live_class()
    {
        var left = new RunUsage
        {
            InputTokens = 100,
            OutputTokens = 10,
            TotalTokens = 110,
            CacheWriteInputTokens = 60,
            CacheWriteLongInputTokens = 30,
        };

        var right = new RunUsage
        {
            InputTokens = 50,
            OutputTokens = 5,
            TotalTokens = 55,
            CacheWriteInputTokens = 20,
            CacheWriteLongInputTokens = 7,
        };

        var sum = RunUsageSum.Add(left, right).ShouldNotBeNull();

        sum.CacheWriteInputTokens.ShouldBe(80);
        sum.CacheWriteLongInputTokens.ShouldBe(37);

        // Counted INSIDE the input total: the totals are summed and the writes are not added on top.
        sum.InputTokens.ShouldBe(150);
    }

    [Fact]
    public void A_side_that_reported_no_cache_write_keeps_the_counters_of_the_other_side()
    {
        var reported = new RunUsage { InputTokens = 100, CacheWriteInputTokens = 60, CacheWriteLongInputTokens = 30 };
        var silent = new RunUsage { InputTokens = 50 };

        // The order of the two sides must not matter.
        foreach (var sum in new[] { RunUsageSum.Add(reported, silent), RunUsageSum.Add(silent, reported) })
        {
            sum.ShouldNotBeNull();
            sum.CacheWriteInputTokens.ShouldBe(60);
            sum.CacheWriteLongInputTokens.ShouldBe(30);
        }
    }

    [Fact]
    public void The_two_time_to_live_classes_are_summed_independently()
    {
        var shortOnly = new RunUsage { InputTokens = 100, CacheWriteInputTokens = 60 };
        var longOnly = new RunUsage { InputTokens = 50, CacheWriteLongInputTokens = 7 };

        var sum = RunUsageSum.Add(shortOnly, longOnly).ShouldNotBeNull();

        sum.CacheWriteInputTokens.ShouldBe(60);
        sum.CacheWriteLongInputTokens.ShouldBe(7);
    }

    [Fact]
    public void Neither_side_reporting_a_cache_write_leaves_both_counters_null()
    {
        // "Nobody measured it" must not become "measured, and it was zero".
        var sum = RunUsageSum.Add(new RunUsage { InputTokens = 100 }, new RunUsage { InputTokens = 50 }).ShouldNotBeNull();

        sum.CacheWriteInputTokens.ShouldBeNull();
        sum.CacheWriteLongInputTokens.ShouldBeNull();
        sum.InputTokens.ShouldBe(150);
    }

    [Fact]
    public void A_reported_zero_is_a_measurement_and_stays_zero()
    {
        var sum = RunUsageSum.Add(
            new RunUsage { InputTokens = 100, CacheWriteInputTokens = 0 },
            new RunUsage { InputTokens = 50 }).ShouldNotBeNull();

        sum.CacheWriteInputTokens.ShouldBe(0);
        sum.CacheWriteLongInputTokens.ShouldBeNull();
    }

    [Fact]
    public void A_null_side_returns_the_other_record_with_its_cache_writes_and_two_nulls_return_null()
    {
        var usage = new RunUsage { InputTokens = 100, CacheWriteInputTokens = 60, CacheWriteLongInputTokens = 30 };

        RunUsageSum.Add(usage, null).ShouldBeSameAs(usage);
        RunUsageSum.Add(null, usage).ShouldBeSameAs(usage);
        RunUsageSum.Add(null, null).ShouldBeNull();
    }
}
