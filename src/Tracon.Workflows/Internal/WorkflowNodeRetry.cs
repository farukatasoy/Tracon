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
    private static readonly HashSet<RunErrorClass> TransientClasses =
    [
        RunErrorClass.ProviderError,
        RunErrorClass.ProviderUnavailable,
        RunErrorClass.RateLimited,
        RunErrorClass.Timeout,
    ];

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
                    delay *= policy.BackoffMultiplier;
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
    /// This runs INSIDE an exception filter, and C# swallows an exception
    /// thrown in a filter: the filter simply evaluates to false. Without this
    /// try/catch a throwing classifier meant the node was silently NOT
    /// retried, with nothing logged - while the shipped guide promises that a
    /// classifier that throws hands over to the built-in one and the error is
    /// logged. RunRecordingAgent.ClassifyOrFallback already did this on the
    /// run path; the workflow path is the second, undocumented consumer and it
    /// did not.
    /// </remarks>
    private static bool IsTransient(IRunErrorClassifier classifier, ILogger logger, Exception exception)
    {
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
