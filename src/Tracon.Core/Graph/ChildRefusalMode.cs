namespace Tracon;

/// <summary>
/// What a <see cref="ChildAgentInvoker"/> does when it cannot deliver the
/// sub-agent's answer: the limit, the budget, a provider that never answered,
/// or a tool that waits for approval.
/// </summary>
/// <remarks>
/// A tool that waits for approval is a refusal in both modes, except for a workflow step whose host turns
/// the request into a workflow request and waits for the answer: that step is told so with
/// <c>hostsApproval</c>, not by its mode.
/// </remarks>
internal enum ChildRefusalMode
{
    /// <summary>
    /// The refusal comes back as the sub-agent's answer: the calling MODEL reads
    /// it as a tool result and can try another approach.
    /// </summary>
    ToolResult = 0,

    /// <summary>
    /// The refusal is thrown, failing the step that made the call. For a
    /// workflow step, where no model reads the answer and the next node would
    /// take the refusal's text as content.
    /// </summary>
    Fail = 1,
}
