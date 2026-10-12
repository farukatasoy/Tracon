namespace Tracon;

/// <summary>
/// Retries a single workflow function node on a transient provider error
/// instead of failing the whole run.
/// </summary>
/// <remarks>
/// <para>
/// Applies only to a node registered with
/// <see cref="TraconWorkflowFunctionExtensions.AddWorkflowFunction{TInput, TOutput}"/>.
/// Whether an error is transient is read from the registered
/// <c>IRunErrorClassifier</c> (the same taxonomy <c>runs.error_class</c>
/// uses) — a provider error, an open circuit breaker, a rate limit, or a
/// timeout is retried; every other class (a compilation failure, a blocked
/// tool, a budget being exceeded, cancellation, …) is not, since retrying
/// those cannot change the outcome.
/// </para>
/// <para>
/// A failure that carries a permanent HTTP status — any 4xx except 408
/// (request timeout) and 429 (rate limit) — is never retried, whatever its
/// class. A provider error can be a 4xx (for example a 401 or a 404), and the
/// same request gets the same answer again; a retried 409 on a
/// non-idempotent call can also repeat its side effect. The status is read
/// from <see cref="System.Net.Http.HttpRequestException.StatusCode"/> or from
/// an <c>HTTP nnn</c> text in the exception chain, and a registered
/// <c>IRunErrorClassifier</c> cannot override this rule.
/// </para>
/// <para>
/// The retry loop runs entirely INSIDE the node's own call — Microsoft
/// Agent Framework calls the node's handler once per routed message and
/// sees only its final outcome. A retried attempt is therefore invisible to
/// <c>TraconWorkflowOptions.MaxSuperSteps</c>: the node still counts as
/// exactly one super-step no matter how many attempts it took.
/// </para>
/// </remarks>
public sealed class WorkflowNodeRetryPolicy
{
    /// <summary>The most attempts a single call may take. One means no retry.</summary>
    public int MaxAttempts { get; set; } = 1;

    /// <summary>The delay before the second attempt.</summary>
    /// <remarks>
    /// Must be from zero to about 49.7 days; registration refuses any other value. A
    /// grown delay is capped at about 49.7 days.
    /// </remarks>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The multiplier applied to the delay after each further attempt.</summary>
    /// <remarks>Must be a finite, non-negative number; registration refuses any other value.</remarks>
    public double BackoffMultiplier { get; set; } = 2.0;
}
