namespace Tracon;

/// <summary>
/// One edge of a <see cref="WorkflowKind.Handoff"/> workflow: the agent <c>From</c> may hand the
/// conversation off to the agent <c>To</c>.
/// </summary>
/// <remarks>
/// <para>
/// The edges are <strong>data</strong>: a name pair and a reason. Nothing executable is stored with an
/// edge (no condition, no code), so the list is safe to export, import and show in the console. Who may
/// hand off to whom is the whole of the topology; the model of the handing-off agent decides when.
/// </para>
/// <para>
/// An edge is one direction. <c>billing → frontline</c> does not let <c>frontline</c> hand off to
/// <c>billing</c>; the reverse edge is a second entry.
/// </para>
/// </remarks>
public sealed record WorkflowHandoff
{
    /// <summary>
    /// Gets the name of the agent that may hand off. One of <c>AgentNames</c>.
    /// </summary>
    public required string From { get; init; }

    /// <summary>
    /// Gets the name of the agent that receives the conversation. One of
    /// <c>AgentNames</c>, and not the same as <c>From</c>.
    /// </summary>
    public required string To { get; init; }

    /// <summary>
    /// Gets why the <c>From</c> agent would hand off to <c>To</c>, in words the model reads
    /// as the description of the handoff tool. <see langword="null"/> uses the description of the target
    /// agent, or its name when it has none.
    /// </summary>
    /// <remarks>
    /// The reason is a hint the model reads, not a guarantee: a model may hand off without it, or not
    /// hand off though it fits.
    /// </remarks>
    public string? Reason { get; init; }
}
