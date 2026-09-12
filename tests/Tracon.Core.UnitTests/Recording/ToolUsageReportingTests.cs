using Microsoft.Extensions.AI;
using Shouldly;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// Verifies that a non-token measurement reported by a tool is linked to the
/// correct call record.
/// </summary>
/// <remarks>
/// The channel was designed by measuring (2026-08-05): <c>AIFunctionArguments.Context</c>
/// arrives as <see langword="null"/> and does not carry the call id;
/// <c>FunctionInvokingChatClient.CurrentContext</c>, on the other hand, is populated
/// inside the tool body. The measurement is therefore keyed by the call id.
/// </remarks>
public sealed class ToolUsageReportingTests
{
    [Fact]
    public void Reported_measurement_is_retrieved_with_the_same_call_id()
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

        // The retrieved measurement is REMOVED from the dictionary: the same record is not written twice.
        accumulator.Take("call-1").ShouldBeNull();
    }

    [Fact]
    public void Another_calls_measurement_is_not_retrieved()
    {
        var accumulator = new ToolUsageAccumulator();
        accumulator.Report("call-1", new ToolCallUsage { Unit = ToolUsageUnits.Seconds, Quantity = 3m });

        accumulator.Take("call-2").ShouldBeNull();
    }

    [Fact]
    public void Tracker_links_the_measurement_to_the_call_record()
    {
        var accumulator = new ToolUsageAccumulator();
        var runId = TraconId.NewId();
        var tracker = new ToolInvocationTracker(runId, measureDuration: false, TimeProvider.System, accumulator);

        tracker.OnCall(new FunctionCallContent("call-voice", "speak", arguments: null), source: null, arguments: null);

        accumulator.Report("call-voice", new ToolCallUsage
        {
            Unit = ToolUsageUnits.Characters,
            Quantity = 42m,
            IsEstimated = true,
        });

        var record = tracker.OnResult(new FunctionResultContent("call-voice", "ok"));

        record.Usage.ShouldNotBeNull();
        record.Usage.Quantity.ShouldBe(42m);
        record.Usage.IsEstimated.ShouldBeTrue();
    }

    [Fact]
    public void A_call_that_reports_no_measurement_is_recorded_with_an_empty_measurement()
    {
        var accumulator = new ToolUsageAccumulator();
        var tracker = new ToolInvocationTracker(
            TraconId.NewId(),
            measureDuration: false,
            TimeProvider.System,
            accumulator);

        tracker.OnCall(new FunctionCallContent("call-1", "get_order", arguments: null), source: null, arguments: null);

        tracker.OnResult(new FunctionResultContent("call-1", "in transit")).Usage.ShouldBeNull();
    }

    [Fact]
    public void Reporting_outside_a_run_fails_silently()
    {
        // Observability functionality does NOT break the tool: if reporting is
        // unavailable, the tool still runs.
        TraconRunContext.SetCurrent(null);

        TraconToolUsage
            .Report(new ToolCallUsage { Unit = ToolUsageUnits.Characters, Quantity = 1m })
            .ShouldBeFalse();
    }

    [Fact]
    public void Reporting_outside_a_tool_context_fails_silently()
    {
        // A scope exists, but the call does not come from a tool body: there is
        // no call id to link to.
        TraconRunContext.SetCurrent(new AgentRunScope
        {
            RunId = TraconId.NewId(),
            RootRunId = TraconId.NewId(),
        });

        try
        {
            TraconToolUsage
                .Report(new ToolCallUsage { Unit = ToolUsageUnits.Characters, Quantity = 1m })
                .ShouldBeFalse();
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }
}
