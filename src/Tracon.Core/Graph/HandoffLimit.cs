using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Counts the handoffs one Handoff workflow run makes and says when the run made more than its limit allows
/// (<see cref="WorkflowDefinition.MaxHandoffs"/>).
/// </summary>
/// <remarks>
/// <para>
/// An edge list lets two agents hand off to one another, and Microsoft Agent Framework does not count
/// handoffs: a loop between them is bounded by nothing but the run's time and budget. This counter is the
/// bound the definition gives.
/// </para>
/// <para>
/// <strong>The counter is an object of the run, not an ambient value.</strong> The workflow runner builds it
/// once and puts it on the run's scope (<see cref="AgentRunScope.Handoffs"/>); the wrapper of every step reads
/// it from the scope and adds to the same instance, so the count survives however the steps are scheduled.
/// An <see cref="AsyncLocal{T}"/> would not carry a count back to the run: a write does not flow to the caller.
/// A run that resumes is a new run and builds a new counter.
/// </para>
/// <para>
/// A handoff is a call to a tool whose name starts with <see cref="ToolPrefix"/>: the framework gives the
/// handing-off agent one declaration-only tool per target and reads the call itself. The wrapper sees the call on
/// its way up and stops the run before the framework acts on it.
/// </para>
/// </remarks>
internal sealed class HandoffLimit
{
    /// <summary>
    /// The prefix of the tool name a handoff call carries. The same value as the framework's
    /// <c>HandoffWorkflowBuilderCore.FunctionPrefix</c>; a test in <c>Tracon.Workflows.UnitTests</c> pins the two together.
    /// </summary>
    public const string ToolPrefix = "handoff_to_";

    /// <summary>The limit of a definition that sets none (<see cref="WorkflowDefinition.MaxHandoffs"/>).</summary>
    public const int DefaultMax = 10;

    private int _made;

    /// <summary>Creates the counter of a definition's run.</summary>
    /// <param name="definition">The Handoff definition the run was compiled from.</param>
    /// <returns>A counter for <see cref="WorkflowDefinition.MaxHandoffs"/>, or for <see cref="DefaultMax"/> when the definition sets none.</returns>
    /// <remarks>
    /// A non-positive stored value gets the default here instead of throwing: the compiler refuses the
    /// definition (<c>WorkflowDefinitionValidator.Require</c>) before any step runs, and that refusal is the
    /// failure the run should end with, not an exception from building its scope.
    /// </remarks>
    public static HandoffLimit For(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new HandoffLimit(definition.MaxHandoffs is > 0 and var max ? max : DefaultMax);
    }

    /// <summary>Creates a counter.</summary>
    /// <param name="max">The most handoffs the run may make. Positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="max"/> is not positive.</exception>
    public HandoffLimit(int max)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(max, 0);

        Max = max;
    }

    /// <summary>Gets the most handoffs the run may make.</summary>
    public int Max { get; }

    /// <summary>Gets how many handoffs the run has made, the one that went over the limit included.</summary>
    public int Made => Volatile.Read(ref _made);

    /// <summary>Adds the handoff calls an answer holds to the count.</summary>
    /// <param name="contents">The content of an update or a message the step's agent produced.</param>
    /// <returns><see langword="true"/> when the count is now over <see cref="Max"/>.</returns>
    public bool RecordAndExceeds(IEnumerable<AIContent> contents)
    {
        ArgumentNullException.ThrowIfNull(contents);

        var calls = 0;

        foreach (var content in contents)
        {
            if (content is FunctionCallContent call && call.Name.StartsWith(ToolPrefix, StringComparison.Ordinal))
            {
                calls++;
            }
        }

        return calls > 0 && Interlocked.Add(ref _made, calls) > Max;
    }
}
