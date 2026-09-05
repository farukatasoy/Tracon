namespace AgentPrism.Core.UnitTests.Runs;

/// <summary>
/// Pins the wire contract of <see cref="RunEventType"/>.
/// </summary>
/// <remarks>
/// <c>run_events.type</c> is a <c>smallint</c> column in every SQL store; the
/// enum's numeric value is what is stored. A member may never be renumbered —
/// doing so would silently reinterpret every already-persisted event of the
/// shifted type. This test fails on any change to the set, forcing a
/// renumbering attempt to be a deliberate, reviewed decision.
/// </remarks>
public sealed class RunEventTypeTests
{
    private static readonly (string Name, int Value)[] Expected =
    [
        ("RunStarted", 0),
        ("MessageDelta", 1),
        ("MessageCompleted", 2),
        ("ToolInvoking", 3),
        ("ToolInvoked", 4),
        ("ToolFailed", 5),
        ("RunCompleted", 6),
        ("RunFailed", 7),
        ("ChildRunStarted", 8),
        ("ChildRunCompleted", 9),
        ("HistoryCompacted", 10),
        ("WorkflowStarted", 11),
        ("SuperStepStarted", 12),
        ("SuperStepCompleted", 13),
        ("ExecutorInvoked", 14),
        ("ExecutorCompleted", 15),
        ("ExecutorFailed", 16),
        ("WorkflowOutput", 17),
        ("WorkflowRequest", 18),
        ("RunAwaitingInput", 19),
        ("ContentMasked", 20),
        ("ContentBlocked", 21),
        ("ModelFallbackUsed", 22),
        ("ReasoningDelta", 23),
        ("DocumentAttached", 24),
        ("RunContinuationBlocked", 25),
        ("ToolOutputTruncated", 26),
        ("StructuredResponseRejected", 27),
        ("StructuredResponseRepairAttempted", 28),
        ("Custom", 29),
        ("ChildRunTimedOut", 30),
    ];

    [Fact]
    public void Numeric_values_match_the_persisted_contract()
    {
        var actual = Enum.GetValues<RunEventType>()
            .Select(static value => (Name: value.ToString(), Value: (int)value))
            .OrderBy(static entry => entry.Value)
            .ToArray();

        actual.ShouldBe(Expected.OrderBy(static entry => entry.Value).ToArray());
    }

    [Fact]
    public void Custom_stays_29()
    {
        // 144.3: the new member (ChildRunTimedOut) had to land AFTER Custom,
        // never between it and its predecessor.
        ((int)RunEventType.Custom).ShouldBe(29);
    }
}
