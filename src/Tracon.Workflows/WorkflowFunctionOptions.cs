namespace Tracon;

/// <summary>
/// The optional settings of a workflow function registered with
/// <c>AddWorkflowFunction</c>: its description, retry policy, timeout and whether it
/// starts when the call tree's budget is spent.
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

    /// <summary>
    /// Gets or sets whether the node starts even when the call tree's cost or time budget is
    /// already spent. <see langword="false"/> (the default) refuses it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A function node is refused at its start once <c>Tracon:AgentGraph:MaxTotalCost</c> or
    /// <c>Tracon:AgentGraph:MaxDuration</c> is passed: the run ends <c>Failed</c> with
    /// <see cref="TraconRunBudgetExceededException"/> (<c>run_budget_exceeded</c>) and no later
    /// node runs. A node that is already running is not stopped, and the token budget never
    /// stops a node. A host that sets neither ceiling is not affected.
    /// </para>
    /// <para>
    /// Set this to <see langword="true"/> for a node that spends nothing and should still run
    /// after a costly one: a step that stores the output the budget has just paid for. The
    /// node's own reported spend still counts against the budget; only its start is exempt.
    /// The exemption is a property of the node's code, so it is set here and cannot be set by
    /// an operator in a workflow definition.
    /// </para>
    /// </remarks>
    public bool StartsWhenBudgetIsSpent { get; set; }
}
