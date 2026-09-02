using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Recording;

public sealed class SideChannelUsageAccumulatorTests
{
    [Fact]
    public void Bos_toplayici_null_dondurur()
    {
        var accumulator = new SideChannelUsageAccumulator();

        accumulator.ToRunUsage().ShouldBeNull();
    }

    [Fact]
    public void Null_kullanim_yok_sayilir()
    {
        var accumulator = new SideChannelUsageAccumulator();

        accumulator.Add(null);

        accumulator.ToRunUsage().ShouldBeNull();
    }

    [Fact]
    public void Birden_fazla_ekleme_toplanir()
    {
        var accumulator = new SideChannelUsageAccumulator();

        accumulator.Add(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 2, TotalTokenCount = 12 });
        accumulator.Add(new UsageDetails { InputTokenCount = 5, OutputTokenCount = 1, TotalTokenCount = 6 });

        var usage = accumulator.ToRunUsage();

        usage.ShouldNotBeNull();
        usage!.InputTokens.ShouldBe(15);
        usage.OutputTokens.ShouldBe(3);
        usage.TotalTokens.ShouldBe(18);
    }

    [Fact]
    public void An_unreported_breakdown_counter_stays_null_rather_than_becoming_zero()
    {
        // 🚨 The summarization side channel must not invent a measurement. If it
        // reported zero here, every run that compacts its context would claim an
        // observed 0% cache hit rate even on providers that report nothing.
        var accumulator = new SideChannelUsageAccumulator();

        accumulator.Add(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 2, TotalTokenCount = 12 });

        var usage = accumulator.ToRunUsage();

        usage.ShouldNotBeNull();
        usage!.CachedInputTokens.ShouldBeNull();
        usage.ReasoningTokens.ShouldBeNull();
        usage.AudioInputTokens.ShouldBeNull();
        usage.AudioOutputTokens.ShouldBeNull();
    }

    [Fact]
    public void Reported_breakdown_counters_accumulate_across_calls()
    {
        var accumulator = new SideChannelUsageAccumulator();

        accumulator.Add(new UsageDetails
        {
            InputTokenCount = 10,
            CachedInputTokenCount = 4,
            ReasoningTokenCount = 1,
        });

        accumulator.Add(new UsageDetails
        {
            InputTokenCount = 5,
            CachedInputTokenCount = 3,
        });

        var usage = accumulator.ToRunUsage();

        usage.ShouldNotBeNull();
        usage!.CachedInputTokens.ShouldBe(7);

        // Only the first call reported reasoning tokens; the second reporting
        // nothing must not reset the total to null.
        usage.ReasoningTokens.ShouldBe(1);
    }

    [Fact]
    public void A_reported_zero_alone_is_enough_to_produce_a_usage_record()
    {
        // Every total is zero, yet the provider DID measure something: a cache
        // miss. Returning null here would throw that observation away.
        var accumulator = new SideChannelUsageAccumulator();

        accumulator.Add(new UsageDetails { CachedInputTokenCount = 0 });

        var usage = accumulator.ToRunUsage();

        usage.ShouldNotBeNull();
        usage!.CachedInputTokens.ShouldBe(0);
        usage.InputTokens.ShouldBeNull();
    }
}
