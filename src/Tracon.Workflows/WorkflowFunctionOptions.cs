namespace Tracon;

/// <summary>
/// The optional settings of a workflow function registered with
/// <c>AddWorkflowFunction&lt;TInput, TOutput, TSettings&gt;</c>.
/// </summary>
/// <remarks>
/// A callback with properties rather than optional parameters: a public method
/// that carries optional parameters freezes its signature at the first stable
/// release, and the next setting would need one more overload - the same
/// reason <c>AddTool</c> takes a <see cref="Action{T}"/> over its own options type.
/// </remarks>
public sealed class WorkflowFunctionOptions
{
    /// <summary>Gets or sets the short description shown in the function catalog.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the policy that retries this node on a transient provider error
    /// instead of failing the whole run. <see langword="null"/> (the default) means
    /// no retry. Every attempt sees the same settings. See
    /// <see cref="WorkflowNodeRetryPolicy"/>.
    /// </summary>
    public WorkflowNodeRetryPolicy? RetryPolicy { get; set; }
}
