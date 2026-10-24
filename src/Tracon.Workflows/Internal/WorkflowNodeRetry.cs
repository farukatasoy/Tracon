using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon;

/// <summary>Wraps a workflow function handler with <see cref="WorkflowNodeRetryPolicy"/>.</summary>
internal static class WorkflowNodeRetry
{
    /// <summary>
    /// The <see cref="RunErrorClass"/> values a retry can plausibly fix — the
    /// provider itself is unavailable, throttling, or slow. Every other
    /// class (a compilation failure, a blocked tool, a budget being
    /// exceeded, …) describes a condition retrying does not change.
    /// </summary>
    /// <remarks>
    /// <see cref="RunErrorClass.ProviderError"/> also holds a permanent 4xx,
    /// so the class is read only after <see cref="IsTransient"/> has ruled
    /// one out.
    /// </remarks>
    private static readonly HashSet<RunErrorClass> TransientClasses =
    [
        RunErrorClass.ProviderError,
        RunErrorClass.ProviderUnavailable,
        RunErrorClass.RateLimited,
        RunErrorClass.Timeout,
    ];

    /// <summary>Checks a policy when its node is registered.</summary>
    /// <param name="policy">The policy.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="WorkflowNodeRetryPolicy.InitialDelay"/> is negative or longer than a delay
    /// can wait, or <see cref="WorkflowNodeRetryPolicy.BackoffMultiplier"/> is negative or
    /// not a finite number.
    /// </exception>
    /// <remarks>
    /// The policy is code, and it used to meet the timer only on a retry, in the
    /// middle of a run. A negative multiplier made the second delay negative and the
    /// retry threw instead of retrying.
    /// </remarks>
    public static void Validate(WorkflowNodeRetryPolicy policy)
    {
        if (policy.InitialDelay < TimeSpan.Zero || policy.InitialDelay.TotalMilliseconds > TimerPeriod.MaxMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                policy.InitialDelay,
                $"{nameof(WorkflowNodeRetryPolicy)}.{nameof(WorkflowNodeRetryPolicy.InitialDelay)} must be between 0 and " +
                $"{TimerPeriod.MaxMilliseconds} milliseconds. Actual value: {policy.InitialDelay}.");
        }

        if (!double.IsFinite(policy.BackoffMultiplier) || policy.BackoffMultiplier < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                policy.BackoffMultiplier,
                $"{nameof(WorkflowNodeRetryPolicy)}.{nameof(WorkflowNodeRetryPolicy.BackoffMultiplier)} must be a finite, " +
                $"non-negative number. Actual value: {policy.BackoffMultiplier}.");
        }
    }

    public static Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>> Wrap<TInput, TOutput>(
        Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>> handler,
        WorkflowNodeRetryPolicy policy,
        IRunErrorClassifier classifier,
        TimeProvider timeProvider,
        ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;

        return async (input, context, cancellationToken) =>
        {
            var attempt = 0;
            var delay = policy.InitialDelay;

            while (true)
            {
                attempt++;

                try
                {
                    return await handler(input, context, cancellationToken).ConfigureAwait(false);
                }
                // 🚨 `!cancellationToken.IsCancellationRequested`, not
                // `is not OperationCanceledException`. Timeout is in
                // TransientClasses above - it is one of the four things a retry
                // can plausibly fix - but HttpClient reports its own request
                // timeout as a TaskCanceledException, so the old filter threw
                // away exactly the failure this policy exists for. The node ran
                // once and gave up. Same class as K-737, found by the phase 157
                // audit; the caller's token is what says whether anything was
                // actually cancelled.
                catch (Exception exception) when (
                    !cancellationToken.IsCancellationRequested &&
                    attempt < policy.MaxAttempts &&
                    IsTransient(classifier, logger, exception))
                {
                    await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);

                    // F-280: capped where the delay can still wait. Unbounded, the
                    // default doubling passed the timer's ceiling after about 22
                    // attempts and the next delay threw.
                    delay = TimeSpan.FromMilliseconds(
                        Math.Min(delay.TotalMilliseconds * policy.BackoffMultiplier, TimerPeriod.MaxMilliseconds));
                }
            }
        };
    }

    /// <summary>
    /// Classifies a node failure, falling back to the built-in classifier when
    /// the registered one throws.
    /// </summary>
    /// <param name="classifier">The registered classifier.</param>
    /// <param name="logger">Where a classifier failure is reported.</param>
    /// <param name="exception">The failure being classified.</param>
    /// <returns><see langword="true"/> when a retry can plausibly fix it.</returns>
    /// <remarks>
    /// <para>
    /// A permanent 4xx is never retried, and it is checked BEFORE any
    /// classifier runs. <see cref="RunErrorClass.ProviderError"/> holds every
    /// provider HTTP error, 4xx included, and it is in
    /// <see cref="TransientClasses"/>. A registered classifier cannot override
    /// the status, in the same way that no classifier can make a cancellation
    /// retry in the fallback chain: the status is a fact about the request,
    /// not an opinion about the fault.
    /// </para>
    /// <para>
    /// This runs INSIDE an exception filter, and C# swallows an exception
    /// thrown in a filter: the filter simply evaluates to false. Without this
    /// try/catch a throwing classifier meant the node was silently NOT
    /// retried, with nothing logged - while the shipped guide promises that a
    /// classifier that throws hands over to the built-in one and the error is
    /// logged. RunRecordingAgent.ClassifyOrFallback already did this on the
    /// run path; the workflow path is the second, undocumented consumer and it
    /// did not.
    /// </para>
    /// </remarks>
    private static bool IsTransient(IRunErrorClassifier classifier, ILogger logger, Exception exception)
    {
        // 🚨 F-338: the class alone decided this, and ProviderError holds every
        // provider HTTP error (K-817). A node that got a 401 ran MaxAttempts
        // times, and a retried 409 on a non-idempotent call repeated its side
        // effect. The raw status is read too: a node can call an HTTP API that
        // never passes the model-call normalizer.
        //
        // 🚨 F-429/F-442: a failure whose stable identity says the next attempt
        // gets the same answer (a tenant's refusal, a truncated structured answer,
        // a compilation failure) is never retried either. The class of a refusal
        // came from its TEXT, and a tenant or provider name with "timeout" in it
        // made the node retry a call its tenant forbids.
        if (ProviderHttpStatus.IsPermanentFailure(exception) || FinalFailure.IsFinal(exception))
        {
            return false;
        }

        // F-407: a spent budget stays spent for the run (cost and time only grow), so a retry
        // cannot fix it. The classifier is not asked: a consumer's classifier that files
        // `run_budget_exceeded` under a transient class would make the node wait and ask
        // MaxAttempts times for the same answer. Local to the retry on purpose; FinalFailure
        // is also read by online evaluation, whose new run opens a new budget where the
        // refusal can change.
        //
        // F-529: a tenant's spent quota is the same. It resets when its period ends, not between two
        // attempts a few milliseconds apart.
        if (exception is TraconRunBudgetExceededException or TraconTenantQuotaExceededException)
        {
            return false;
        }

        var error = ToRunError(exception);
        RunErrorClassification classification;

        try
        {
            classification = classifier.Classify(error);
        }
        catch (Exception classifierException)
        {
            logger.LogError(
                classifierException,
                "The registered IRunErrorClassifier threw while classifying a workflow node failure; " +
                "falling back to the built-in classifier.");

            classification = new DefaultRunErrorClassifier().Classify(error);
        }

        return TransientClasses.Contains(classification.Class);
    }

    // Same shape as RunRecordingAgent.ToRunError: a TraconException
    // carries its own stable error type name, everything else falls back to
    // its CLR type name.
    private static RunError ToRunError(Exception exception)
        => new()
        {
            Type = exception is TraconException traconException
                ? traconException.ErrorType
                : exception.GetType().FullName ?? exception.GetType().Name,
            Message = exception.Message,
        };
}
