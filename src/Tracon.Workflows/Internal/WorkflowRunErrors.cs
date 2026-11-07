using System.Reflection;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Turns what a failed workflow run threw into the run error its row carries: the stable error type, the text that is safe
/// to store, and the class and fingerprint the error classifier gives it.
/// </summary>
/// <remarks>
/// <para>
/// A failure is logged once, with its full detail, under a reference; the row, the event table, the stream and every run
/// event sink carry only the display-safe text and that reference. The logger is the workflow runner's own, so the log
/// category of these entries is the runner's.
/// </para>
/// <para>
/// This type holds no run state and opens no span or scope: it can be used from any body of the runner.
/// </para>
/// </remarks>
internal sealed class WorkflowRunErrors
{
    private readonly ILogger _logger;
    private readonly IRunErrorClassifier? _classifier;

    /// <summary>Creates the converter.</summary>
    /// <param name="logger">The logger of the runner that owns the run.</param>
    /// <param name="classifier">The error classifier. When <see langword="null"/>, an error carries no class.</param>
    public WorkflowRunErrors(ILogger logger, IRunErrorClassifier? classifier)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _classifier = classifier;
    }

    /// <summary>
    /// Converts an exception into a run error; strips reflection/handler-invocation
    /// wrappers.
    /// </summary>
    /// <remarks>
    /// MAF's internal execution pipeline (for example a Magentic turn-token /
    /// external-response handler) can throw an exception wrapped in a
    /// <see cref="TargetInvocationException"/> or a single-element
    /// <see cref="AggregateException"/>. The wrapped message carries only a
    /// meaningless text like "Error invoking handler for ..."; the real cause
    /// stays in <c>InnerException</c>, and if written unwrapped the operator
    /// never sees the actual fault at all.
    /// Only SINGLE-layer, single-inner-exception wrappers are stripped — a
    /// direct code error (for example a genuine `AggregateException` with
    /// multiple inner exceptions) is left as is.
    /// </remarks>
    internal RunError ToRunError(Exception exception, Guid runId, (Exception Exception, string CorrelationId)? logged = null)
    {
        var unwrapped = UnwrapInvocation(exception);
        string correlationId;

        if (logged is { } previous && ReferenceEquals(previous.Exception, unwrapped))
        {
            // Already logged with its detail when its event was written; the
            // row names the same entry instead of logging it again.
            correlationId = previous.CorrelationId;
        }
        else
        {
            correlationId = SafeErrorText.NewCorrelationId();
            _logger.LogError(unwrapped, "Workflow run {RunId} failed. (ref: {CorrelationId})", runId, correlationId);
        }

        // 🚨 The SAME stable identity the agent run path writes: a
        // TraconException's ErrorType, not its CLR name. The class is added
        // where the row closes (CompleteAsync), for every error, not only this one.
        return new RunError
        {
            Type = unwrapped is TraconException traconException
                ? traconException.ErrorType
                : unwrapped.GetType().FullName ?? unwrapped.GetType().Name,
            Message = SafeErrorText.ForPersistence(unwrapped, correlationId),
        };
    }

    /// <summary>
    /// Fills <see cref="RunError.Class"/> and <see cref="RunError.Fingerprint"/>,
    /// falling back to the built-in classifier when a consumer's throws.
    /// </summary>
    internal RunError Classify(RunError error)
    {
        if (_classifier is null)
        {
            return error;
        }

        RunErrorClassification classification;

        try
        {
            classification = _classifier.Classify(error);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "The registered IRunErrorClassifier threw while classifying a workflow run error; falling back to the built-in classifier.");

            classification = new DefaultRunErrorClassifier().Classify(error);
        }

        return error with { Class = classification.Class, Fingerprint = classification.Fingerprint };
    }

    /// <summary>Strips one reflection/handler-invocation wrapper (the rule <c>ToRunError</c> describes).</summary>
    internal static Exception UnwrapInvocation(Exception exception)
        => exception switch
        {
            TargetInvocationException { InnerException: { } inner } => inner,
            AggregateException { InnerExceptions.Count: 1 } aggregate => aggregate.InnerExceptions[0],
            _ => exception,
        };

    /// <summary>The exception a failure event reports, or <see langword="null"/> for any other event.</summary>
    internal static Exception? FailureOf(WorkflowEvent workflowEvent)
        => workflowEvent switch
        {
            ExecutorFailedEvent failed => failed.Data,
            WorkflowErrorEvent error => error.Exception,
            _ => null,
        };

    /// <summary>
    /// Logs a failure event's exception with its full detail, once, and returns
    /// the exception and the ref its display-safe text carries.
    /// </summary>
    /// <remarks>
    /// The event table, the stream and every run event sink get only
    /// <see cref="SafeErrorText.ForPersistence"/>; the detail lives in the log
    /// under the ref. An exception already logged keeps its ref.
    /// </remarks>
    internal (Exception Exception, string CorrelationId) LogFailure(
        Exception failure,
        Guid runId,
        WorkflowEvent workflowEvent,
        (Exception Exception, string CorrelationId)? previous)
    {
        var unwrapped = UnwrapInvocation(failure);

        if (previous is { } logged && ReferenceEquals(logged.Exception, unwrapped))
        {
            return logged;
        }

        var correlationId = SafeErrorText.NewCorrelationId();

        _logger.LogError(
            unwrapped,
            "Workflow run {RunId} node {ExecutorId} failed. (ref: {CorrelationId})",
            runId,
            (workflowEvent as ExecutorFailedEvent)?.ExecutorId,
            correlationId);

        return (unwrapped, correlationId);
    }

    /// <summary>Converts a graph-level error into a run error.</summary>
    internal RunError ToRunError(WorkflowErrorEvent failure, Guid runId, (Exception Exception, string CorrelationId)? logged = null)
        => failure.Exception is { } exception
            ? ToRunError(exception, runId, logged)
            : new RunError
            {
                Type = nameof(WorkflowErrorEvent),
                Message = "Workflow execution stopped with an error.",
            };
}
