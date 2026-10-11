namespace Tracon;

/// <summary>
/// What a <see cref="ChildAgentInvoker"/> does when it cannot deliver the
/// sub-agent's answer: the limit, the budget, a provider that never answered,
/// or a tool that waits for approval.
/// </summary>
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
