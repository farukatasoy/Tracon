using Microsoft.Extensions.AI;
using Shouldly;

namespace AgentPrism.Core.UnitTests.Recording;

/// <summary>
/// Bir tool'un bildirdigi token disi olcumun dogru cagri kaydina baglandigini
/// dogrular.
/// </summary>
/// <remarks>
/// Kanal olculerek tasarlandi (2026-08-05): <c>AIFunctionArguments.Context</c>
/// <see langword="null"/> gelir ve cagri kimligini tasimaz;
/// <c>FunctionInvokingChatClient.CurrentContext</c> ise tool govdesinde doludur.
/// Olcum bu yuzden cagri kimligiyle anahtarlanir.
/// </remarks>
public sealed class ToolUsageReportingTests
{
    [Fact]
    public void Bildirilen_olcum_ayni_cagri_kimligiyle_alinir()
    {
        var accumulator = new ToolUsageAccumulator();

        var usage = new ToolCallUsage
        {
            Unit = ToolUsageUnits.Characters,
            Quantity = 120m,
            Cost = 0.0132m,
            Currency = "USD",
        };

        accumulator.Report("call-1", usage);

        accumulator.Take("call-1").ShouldBe(usage);

        // Alinan olcum sozlukten CIKARILIR: ayni kayit iki kez yazilmaz.
        accumulator.Take("call-1").ShouldBeNull();
    }

    [Fact]
    public void Baska_bir_cagrinin_olcumu_alinmaz()
    {
        var accumulator = new ToolUsageAccumulator();
        accumulator.Report("call-1", new ToolCallUsage { Unit = ToolUsageUnits.Seconds, Quantity = 3m });

        accumulator.Take("call-2").ShouldBeNull();
    }

    [Fact]
    public void Izleyici_olcumu_cagri_kaydina_baglar()
    {
        var accumulator = new ToolUsageAccumulator();
        var runId = AgentPrismId.NewId();
        var tracker = new ToolInvocationTracker(runId, measureDuration: false, TimeProvider.System, accumulator);

        tracker.OnCall(new FunctionCallContent("call-ses", "speak", arguments: null), source: null, arguments: null);

        accumulator.Report("call-ses", new ToolCallUsage
        {
            Unit = ToolUsageUnits.Characters,
            Quantity = 42m,
            IsEstimated = true,
        });

        var record = tracker.OnResult(new FunctionResultContent("call-ses", "tamam"));

        record.Usage.ShouldNotBeNull();
        record.Usage.Quantity.ShouldBe(42m);
        record.Usage.IsEstimated.ShouldBeTrue();
    }

    [Fact]
    public void Olcum_bildirmeyen_cagri_bos_olcumle_kaydedilir()
    {
        var accumulator = new ToolUsageAccumulator();
        var tracker = new ToolInvocationTracker(
            AgentPrismId.NewId(),
            measureDuration: false,
            TimeProvider.System,
            accumulator);

        tracker.OnCall(new FunctionCallContent("call-1", "get_order", arguments: null), source: null, arguments: null);

        tracker.OnResult(new FunctionResultContent("call-1", "kargoda")).Usage.ShouldBeNull();
    }

    [Fact]
    public void Calistirma_disinda_bildirim_sessizce_basarisiz_olur()
    {
        // Gozlemlenebilirlik islevselligi BOZMAZ: bildirim yapilamiyorsa tool
        // yine calisir.
        AgentPrismRunContext.SetCurrent(null);

        AgentPrismToolUsage
            .Report(new ToolCallUsage { Unit = ToolUsageUnits.Characters, Quantity = 1m })
            .ShouldBeFalse();
    }

    [Fact]
    public void Tool_baglami_disinda_bildirim_sessizce_basarisiz_olur()
    {
        // Kapsam var ama cagri bir tool govdesinden gelmiyor: baglanacak bir
        // cagri kimligi yoktur.
        AgentPrismRunContext.SetCurrent(new AgentRunScope
        {
            RunId = AgentPrismId.NewId(),
            RootRunId = AgentPrismId.NewId(),
        });

        try
        {
            AgentPrismToolUsage
                .Report(new ToolCallUsage { Unit = ToolUsageUnits.Characters, Quantity = 1m })
                .ShouldBeFalse();
        }
        finally
        {
            AgentPrismRunContext.SetCurrent(null);
        }
    }
}
