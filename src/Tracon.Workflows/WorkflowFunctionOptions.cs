namespace Tracon;

/// <summary>
/// The optional settings of a workflow function registered with
/// <c>AddWorkflowFunction</c>: its description, retry policy, timeout, how many calls may run at
/// once and whether it starts when the call tree's budget is spent.
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
    /// Gets or sets whether the node starts even when the call tree's cost or time budget, or
    /// the tenant's cost quota, is already spent. <see langword="false"/> (the default) refuses it.
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
    /// The ceiling is the logical run's. A run that continues an earlier one (a person's answer, an
    /// operator's resume, a recovery after an interruption) starts from what the earlier runs spent,
    /// and the time they were executing, so a workflow that waits for a person does not get the whole
    /// ceiling again with every answer.
    /// </para>
    /// <para>
    /// The tenant's quota is read at the same place. Once the tenant's cost quota for the current period
    /// is spent, counting what this run has spent that its counter has not seen yet, the node does not start
    /// either: the run ends <c>Failed</c> with <see cref="TraconTenantQuotaExceededException"/>
    /// (<c>tenant_quota_exceeded</c>). The quota resets when its period ends; the budget is a setting.
    /// </para>
    /// <para>
    /// Set this to <see langword="true"/> for a node that spends nothing and should still run
    /// after a costly one: a step that stores the output the budget has just paid for. The
    /// node's own reported spend still counts against the budget and the quota; only its start is exempt.
    /// The exemption is a property of the node's code, so it is set here and cannot be set by
    /// an operator in a workflow definition.
    /// </para>
    /// </remarks>
    public bool StartsWhenBudgetIsSpent { get; set; }

    /// <summary>
    /// Gets or sets how many calls of the function may run at the same time in this process.
    /// <see langword="null"/> (the default) means no limit of its own: only the run slots
    /// (<c>Tracon:Workflows:MaxConcurrentRuns</c>) bound it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a node that is costly to run in parallel: a GPU voice synthesis, a render. The limit
    /// belongs to the function, so it holds across every workflow that uses it and across
    /// every tenant, and it reaches a node of a stored chain and a node of a graph built in code
    /// (<see cref="WorkflowFunctionBinding.GetWorkflowFunction(System.IServiceProvider, string)"/>) alike.
    /// </para>
    /// <para>
    /// A call that finds every slot taken waits. The wait belongs to the run: it ends when the run is
    /// cancelled or reaches its deadline, and it is <em>not</em> counted against
    /// <see cref="Timeout"/>, which bounds the node's own work. Each attempt of a retried node takes a
    /// slot of its own and gives it back before the retry's delay, so a node that waits to retry does
    /// not hold a slot. The budget gate (<see cref="StartsWhenBudgetIsSpent"/>) is read after the slot
    /// is taken, because the budget can run out while the call waits.
    /// </para>
    /// <para>
    /// <strong>The limit is per process.</strong> A host with several replicas allows that many calls
    /// per replica. A handler that ignores its token after a <see cref="Timeout"/> keeps running
    /// without a slot; pass the token on. A wait writes no run event; it is logged at the
    /// <c>Debug</c> level.
    /// </para>
    /// <para>
    /// Must be 1 or more; another value throws <see cref="ArgumentOutOfRangeException"/> when the
    /// function is registered.
    /// </para>
    /// </remarks>
    public int? MaxConcurrency { get; set; }
}
