namespace AgentPrism;

/// <summary>
/// Retries a single workflow function node on a transient provider error
/// instead of failing the whole run.
/// </summary>
/// <remarks>
/// <para>
/// Applies only to a node registered with
/// <see cref="AgentPrismWorkflowFunctionExtensions.AddWorkflowFunction{TInput, TOutput}"/>.
/// Whether an error is transient is read from the registered
/// <c>IRunErrorClassifier</c> (the same taxonomy <c>runs.error_class</c>
/// uses) — a provider error, an open circuit breaker, a rate limit, or a
/// timeout is retried; every other class (a compilation failure, a blocked
/// tool, a budget being exceeded, cancellation, …) is not, since retrying
/// those cannot change the outcome.
/// </para>
/// <para>
/// The retry loop runs entirely INSIDE the node's own call — Microsoft
/// Agent Framework calls the node's handler once per routed message and
/// sees only its final outcome. A retried attempt is therefore invisible to
/// <c>AgentPrismWorkflowOptions.MaxSuperSteps</c>: the node still counts as
/// exactly one super-step no matter how many attempts it took.
/// </para>
/// </remarks>
public sealed class WorkflowNodeRetryPolicy
{
    /// <summary>The most attempts a single call may take. One means no retry.</summary>
    public int MaxAttempts { get; set; } = 1;

    /// <summary>The delay before the second attempt.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The multiplier applied to the delay after each further attempt.</summary>
    public double BackoffMultiplier { get; set; } = 2.0;
}
