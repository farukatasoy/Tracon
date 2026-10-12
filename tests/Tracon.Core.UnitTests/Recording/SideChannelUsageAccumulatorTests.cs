using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Recording;

public sealed class SideChannelUsageAccumulatorTests
{
    [Fact]
    public void Bos_toplayici_null_dondurur()
    {
        var accumulator = new SideChannelUsageAccumulator();

        accumulator.ToAgentModelUsage().ShouldBeNull();
    }

    [Fact]
    public void Null_kullanim_yok_sayilir()
    {
        var accumulator = new SideChannelUsageAccumulator();

        accumulator.Add(null);

        accumulator.ToAgentModelUsage().ShouldBeNull();
    }

    [Fact]
    public void Birden_fazla_ekleme_toplanir()
    {
        var accumulator = new SideChannelUsageAccumulator();

        accumulator.Add(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 2, TotalTokenCount = 12 });
        accumulator.Add(new UsageDetails { InputTokenCount = 5, OutputTokenCount = 1, TotalTokenCount = 6 });

        var usage = accumulator.ToAgentModelUsage();

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

        var usage = accumulator.ToAgentModelUsage();

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

        var usage = accumulator.ToAgentModelUsage();

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

        var usage = accumulator.ToAgentModelUsage();

        usage.ShouldNotBeNull();
        usage!.CachedInputTokens.ShouldBe(0);
        usage.InputTokens.ShouldBeNull();
    }

    /// <summary>
    /// F-349: another model's share is kept apart from the agent's share and
    /// grouped by provider and model, so each share is priced at its own model.
    /// </summary>
    [Fact]
    public void Another_models_usage_is_kept_apart_and_grouped_by_provider_and_model()
    {
        var accumulator = new SideChannelUsageAccumulator();

        accumulator.Add(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 2, TotalTokenCount = 12 });
        accumulator.Add(new UsageDetails { InputTokenCount = 30, OutputTokenCount = 2, TotalTokenCount = 32 }, "judging", "judge-model");
        accumulator.Add(new UsageDetails { InputTokenCount = 40, OutputTokenCount = 3, TotalTokenCount = 43 }, "JUDGING", "Judge-Model");
        accumulator.Add(new UsageDetails { InputTokenCount = 5, OutputTokenCount = 1, TotalTokenCount = 6 }, "acme", "summarizer");
        accumulator.Add(null, "zeta", "ignored");

        accumulator.ToAgentModelUsage().ShouldNotBeNull().TotalTokens.ShouldBe(12);

        var shares = accumulator.ToOtherModelUsages();

        // Case-insensitive grouping, ordered by provider; a null usage adds no share.
        shares.Count.ShouldBe(2);
        shares[0].Provider.ShouldBe("acme");
        shares[0].Usage.TotalTokens.ShouldBe(6);
        shares[1].Provider.ShouldBe("judging");
        shares[1].Usage.InputTokens.ShouldBe(70);
        shares[1].Usage.OutputTokens.ShouldBe(5);
        shares[1].Usage.TotalTokens.ShouldBe(75);
    }

    [Fact]
    public void A_run_with_no_other_model_has_no_other_share()
    {
        var accumulator = new SideChannelUsageAccumulator();

        accumulator.Add(new UsageDetails { InputTokenCount = 10, TotalTokenCount = 10 });

        accumulator.ToOtherModelUsages().ShouldBeEmpty();
    }
}
