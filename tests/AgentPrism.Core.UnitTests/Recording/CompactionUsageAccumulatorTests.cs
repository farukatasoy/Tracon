using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Recording;

public sealed class CompactionUsageAccumulatorTests
{
    [Fact]
    public void Bos_toplayici_null_dondurur()
    {
        var accumulator = new CompactionUsageAccumulator();

        accumulator.ToRunUsage().ShouldBeNull();
    }

    [Fact]
    public void Null_kullanim_yok_sayilir()
    {
        var accumulator = new CompactionUsageAccumulator();

        accumulator.Add(null);

        accumulator.ToRunUsage().ShouldBeNull();
    }

    [Fact]
    public void Birden_fazla_ekleme_toplanir()
    {
        var accumulator = new CompactionUsageAccumulator();

        accumulator.Add(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 2, TotalTokenCount = 12 });
        accumulator.Add(new UsageDetails { InputTokenCount = 5, OutputTokenCount = 1, TotalTokenCount = 6 });

        var usage = accumulator.ToRunUsage();

        usage.ShouldNotBeNull();
        usage!.InputTokens.ShouldBe(15);
        usage.OutputTokens.ShouldBe(3);
        usage.TotalTokens.ShouldBe(18);
    }
}
