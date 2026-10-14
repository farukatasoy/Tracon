namespace Tracon;

// 🚨 F-429: the job worker repeated every failure up to the job's attempt limit.
// An eval whose judge was cut off at its output limit, or a queued run whose model
// rejected the request with a permanent 4xx, gets the same answer every time, and
// each attempt paid for the case's agent run and its judge again. The node retry
// and the online eval had their own answers to the same question, and the node
// retry read a refusal's TEXT (F-442). The question is answered here, once.

/// <summary>
/// Decides whether a failure gets the same answer when the same work is done again.
/// </summary>
/// <remarks>
/// <para>
/// Reads only facts Tracon itself wrote: the stable <see cref="TraconException.ErrorType"/>
/// of a frame in the failure, and the HTTP status a normalized provider failure carries
/// as a property. Never a message: a message can quote a tenant name, an allowed
/// provider or a provider's own text, and a name with "timeout" in it made a tenant's
/// refusal a transient fault.
/// </para>
/// <para>
/// A failure is final when its graph holds:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>output_truncated</c>: the next attempt sends the same output limit and is cut
/// off again.
/// </description></item>
/// <item><description>
/// <c>tenant_provider_refused</c>: the tenant's rules refuse the provider before
/// any call.
/// </description></item>
/// <item><description>
/// <c>upstream_error</c> whose status is a permanent 4xx
/// (<see cref="ProviderHttpStatus.IsPermanent"/>): every 4xx except 408 and 429.
/// </description></item>
/// </list>
/// <para>
/// The job worker fails a job on this attempt when its handler's failure is final, for
/// a consumer's handler too. A consumer's own exception (an <see cref="HttpRequestException"/>
/// it throws while it waits for a resource) carries none of these identities and keeps
/// its retries. The workflow node retry and the online evaluation read the same answer.
/// </para>
/// </remarks>
internal static class FinalFailure
{
    // 🚨 compilation_failed is NOT here. A compilation wraps a loop criterion factory
    // that threw (and the next compilation calls it again, as documented) and any
    // TraconException the model registry raises while it builds a client. Either can
    // be transient; found by the independent review of this round.
    private static readonly HashSet<string> SameAnswerIdentities = new(StringComparer.Ordinal)
    {
        TraconOutputTruncatedException.OutputTruncatedErrorType,
        TraconTenantProviderRefusedException.TenantProviderRefusedErrorType,
    };

    /// <summary>Determines whether repeating the work that failed gets the same answer.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns><see langword="true"/> when a frame of the failure carries a final identity.</returns>
    public static bool IsFinal(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        foreach (var frame in Frames(exception))
        {
            switch (frame)
            {
                case ProviderInvocationException { StatusCode: { } status } when ProviderHttpStatus.IsPermanent(status):
                    return true;

                case TraconException tracon when SameAnswerIdentities.Contains(tracon.ErrorType):
                    return true;

                default:
                    break;
            }
        }

        return false;
    }

    private static IEnumerable<Exception> Frames(Exception exception)
    {
        yield return exception;

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                foreach (var nested in Frames(inner))
                {
                    yield return nested;
                }
            }
        }
        else if (exception.InnerException is { } innerException)
        {
            foreach (var nested in Frames(innerException))
            {
                yield return nested;
            }
        }
    }
}
