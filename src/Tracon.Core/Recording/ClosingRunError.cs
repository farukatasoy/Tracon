namespace Tracon;

/// <summary>The error a run row carries when something other than the run recorder closes it.</summary>
/// <remarks>
/// <para>
/// The queued-run workers, the recovery and reconciliation passes and the endpoints close a row that never started, or one
/// whose recorder is gone. They wrote the CLR short name of the exception as the error type and no class, so the same
/// refusal was <c>TenantProviderRefused</c> on a run that started and <c>Unknown</c> on one that did not, and
/// <c>GET /api/runs?errorType=</c> found one of them.
/// </para>
/// <para>
/// The type is the one every other path writes (<see cref="SafeErrorText.TypeOf"/>). The class and the fingerprint come
/// from the built-in classifier: these paths hold no consumer classifier, and the class of an error must not depend on which
/// path wrote it.
/// </para>
/// </remarks>
internal static class ClosingRunError
{
    /// <summary>Builds the error a closing write carries for <paramref name="exception"/>.</summary>
    /// <param name="exception">The exception the run failed with.</param>
    /// <param name="correlationId">The id of the log entry that carries the exception's full detail.</param>
    /// <returns>The error, classified.</returns>
    public static RunError From(Exception exception, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(correlationId);

        var error = new RunError
        {
            Type = SafeErrorText.TypeOf(exception),
            Message = SafeErrorText.ForPersistence(exception, correlationId),
        };

        var classification = new DefaultRunErrorClassifier().Classify(error);

        return error with { Class = classification.Class, Fingerprint = classification.Fingerprint };
    }
}
