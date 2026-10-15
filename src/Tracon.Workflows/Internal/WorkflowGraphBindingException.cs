namespace Tracon;

/// <summary>
/// A node a graph built in code asked for cannot be bound: the function is not registered, or its
/// settings do not fit.
/// </summary>
/// <remarks>
/// A separate type so the catalog can add what only it knows - the name of the workflow whose factory
/// was running - without the binding helpers needing it. The message names the node and never carries a
/// settings value.
/// </remarks>
internal sealed class WorkflowGraphBindingException : TraconException
{
    /// <summary>Creates a new error.</summary>
    /// <param name="message">The user-facing message. Must not carry a settings value.</param>
    public WorkflowGraphBindingException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The user-facing message. Must not carry a settings value.</param>
    /// <param name="innerException">The underlying error.</param>
    public WorkflowGraphBindingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
