using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Recording;

/// <summary>
/// Verifies that <see cref="ToolInvocationTracker"/> marks a denied or
/// timed-out call distinctly from an ordinary success or failure (phase 69).
/// </summary>
public sealed class ToolInvocationTrackerGovernanceTests
{
    [Fact]
    public void Denied_call_is_marked_but_carries_no_error()
    {
        var authorization = new ToolAuthorizationAccumulator();
        var tracker = new ToolInvocationTracker(
            AgentPrismId.NewId(),
            measureDuration: false,
            TimeProvider.System,
            authorization: authorization);

        tracker.OnCall(new FunctionCallContent("call-1", "cancel_order", arguments: null), source: null, arguments: null);

        // AuthorizingAIFunction returns the denial reason as an ordinary
        // (non-exceptional) result — see docs/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md,
        // section 69.2. The tracker can only tell it apart from a real success
        // through the accumulator marker.
        authorization.RecordDenied("call-1");

        var record = tracker.OnResult(new FunctionResultContent("call-1", "This call was not authorized."));

        record.AuthorizationDenied.ShouldBeTrue();
        record.TimedOut.ShouldBeFalse();
        record.Error.ShouldBeNull();
        record.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Ordinary_success_is_not_marked_as_denied()
    {
        var tracker = new ToolInvocationTracker(
            AgentPrismId.NewId(),
            measureDuration: false,
            TimeProvider.System,
            authorization: new ToolAuthorizationAccumulator());

        tracker.OnCall(new FunctionCallContent("call-1", "get_order", arguments: null), source: null, arguments: null);

        var record = tracker.OnResult(new FunctionResultContent("call-1", "in transit"));

        record.AuthorizationDenied.ShouldBeFalse();
        record.TimedOut.ShouldBeFalse();
    }

    [Fact]
    public void Timeout_exception_is_marked_as_timed_out_and_as_an_error()
    {
        var tracker = new ToolInvocationTracker(AgentPrismId.NewId(), measureDuration: false, TimeProvider.System);

        tracker.OnCall(new FunctionCallContent("call-1", "slow_tool", arguments: null), source: null, arguments: null);

        var timeoutException = new AgentPrismToolTimeoutException("Tool 'slow_tool' did not complete within 1s.")
        {
            ToolName = "slow_tool",
            Timeout = TimeSpan.FromSeconds(1),
        };

        var record = tracker.OnResult(new FunctionResultContent("call-1", result: null) { Exception = timeoutException });

        record.TimedOut.ShouldBeTrue();
        record.AuthorizationDenied.ShouldBeFalse();
        record.Succeeded.ShouldBeFalse();
        record.Error.ShouldBe(timeoutException.Message);
    }

    [Fact]
    public void A_missing_accumulator_never_reports_a_denial()
    {
        // No AuthorizingAIFunction was ever installed for this run; the
        // tracker must not throw and must default to "not denied".
        var tracker = new ToolInvocationTracker(AgentPrismId.NewId(), measureDuration: false, TimeProvider.System);

        tracker.OnCall(new FunctionCallContent("call-1", "get_order", arguments: null), source: null, arguments: null);

        tracker.OnResult(new FunctionResultContent("call-1", "in transit")).AuthorizationDenied.ShouldBeFalse();
    }
}
