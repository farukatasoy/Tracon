namespace Tracon;

/// <summary>
/// The optional settings of a workflow function registered with
/// <c>AddWorkflowFunction</c>: its description, retry policy and timeout.
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

    /// <summary>
    /// Gets or sets the longest time one call of the function may take.
    /// <see langword="null"/> (the default) means no time limit of its own: only the
    /// run's deadline (<c>Tracon:Workflows:RunTimeout</c>) stops it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A node can set its own value (<see cref="WorkflowNodeReference.Timeout"/>); the
    /// node's value wins over this one. When the time is up, the token the handler was
    /// given is cancelled, the run stops waiting for the handler, and the run ends
    /// <c>Failed</c> with <see cref="TraconWorkflowFunctionTimeoutException"/>
    /// (<c>workflow_function_timeout</c>). No later node runs.
    /// </para>
    /// <para>
    /// <strong>Pass the token on.</strong> A handler that ignores its token keeps
    /// running in the background after the run has moved on, and any side effect it
    /// has still happens. What it returns then is discarded; its end is logged.
    /// </para>
    /// <para>
    /// With a <see cref="RetryPolicy"/>, the timeout applies to each attempt and a
    /// timed-out attempt is retried, so the longest a node can take is about
    /// <c>MaxAttempts × Timeout</c> plus the delays between attempts.
    /// </para>
    /// <para>
    /// Must be from one millisecond to about 49.7 days; another value throws
    /// <see cref="ArgumentOutOfRangeException"/> when the function is registered.
    /// </para>
    /// </remarks>
    public TimeSpan? Timeout { get; set; }
}
