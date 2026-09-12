namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// The numeric value of a <see cref="RunStatus"/> member added at the end MUST
/// NOT shift; existing rows stored as <c>smallint</c> in the database depend on
/// these values (see the <c>RunStatus.cs</c> XML docs).
/// </summary>
/// <remarks>
/// The job's own identity is no longer an enum: Phase 137 replaced
/// <c>JobKind</c> with the string <see cref="JobRecord.HandlerKey"/>, so its
/// stability is a matter of the CONSTANTS not changing, which
/// <see cref="JobHandlerKeyTests"/> asserts instead.
/// </remarks>
public sealed class RunStatusEnumTests
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
}
