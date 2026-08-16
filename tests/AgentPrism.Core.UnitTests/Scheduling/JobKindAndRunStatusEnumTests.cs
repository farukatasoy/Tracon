namespace AgentPrism.Core.UnitTests.Scheduling;

/// <summary>
/// The numeric value of an enum member added at the end in Phase 46 MUST NOT
/// shift; existing rows stored as <c>smallint</c> in the database depend on
/// these values (see the <c>RunStatus.cs</c> / <c>JobKind.cs</c> XML docs).
/// </summary>
public sealed class JobKindAndRunStatusEnumTests
{
    [Fact]
    public void RunStatus_values_have_not_shifted()
    {
        ((int)RunStatus.Running).ShouldBe(0);
        ((int)RunStatus.Completed).ShouldBe(1);
        ((int)RunStatus.Failed).ShouldBe(2);
        ((int)RunStatus.Canceled).ShouldBe(3);
        ((int)RunStatus.AwaitingInput).ShouldBe(4);
        ((int)RunStatus.Queued).ShouldBe(5);
    }

    [Fact]
    public void JobKind_values_have_not_shifted()
    {
        ((int)JobKind.AgentBatch).ShouldBe(0);
        ((int)JobKind.Workflow).ShouldBe(1);
        ((int)JobKind.Eval).ShouldBe(2);
        ((int)JobKind.WebhookDelivery).ShouldBe(3);
        ((int)JobKind.Retention).ShouldBe(4);
        ((int)JobKind.AgentRun).ShouldBe(5);
    }
}
