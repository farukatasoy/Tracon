namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// Verifies the ambient-write/scoped-read channel that carries a denial from
/// <see cref="AuthorizingAIFunction"/> to <see cref="ToolInvocationTracker"/>,
/// keyed by call identity — the same pattern <see cref="ToolUsageAccumulator"/>
/// uses for non-token usage (phase 28), applied to an authorization decision.
/// </summary>
public sealed class ToolAuthorizationAccumulatorTests
{
    [Fact]
    public void Denied_call_is_retrieved_with_the_same_call_id()
    {
        var accumulator = new ToolAuthorizationAccumulator();

        accumulator.RecordDenied("call-1");

        accumulator.TakeDenied("call-1").ShouldBeTrue();

        // Taken and removed: the same record is not written twice.
        accumulator.TakeDenied("call-1").ShouldBeFalse();
    }

    [Fact]
    public void Another_calls_denial_is_not_retrieved()
    {
        var accumulator = new ToolAuthorizationAccumulator();
        accumulator.RecordDenied("call-1");

        accumulator.TakeDenied("call-2").ShouldBeFalse();
    }

    [Fact]
    public void A_call_that_was_never_denied_is_not_reported_as_denied()
    {
        var accumulator = new ToolAuthorizationAccumulator();

        accumulator.TakeDenied("call-1").ShouldBeFalse();
    }
}
