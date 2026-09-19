namespace Tracon;

/// <summary>
/// Tells an operation's own cancellation apart from a cancellation that came
/// from somewhere else.
/// </summary>
/// <remarks>
/// <para>
/// <c>exception is not OperationCanceledException</c> is the wrong filter
/// on its own, and it was written that way in nine places. An
/// <see cref="OperationCanceledException"/> only means "we are shutting down"
/// or "the caller cancelled" when OUR token is actually cancelled. Any other
/// one is an ordinary failure wearing the same type.
/// </para>
/// <para>
/// The trigger is not hypothetical. <c>IRunStore</c>, <c>IRunInputStore</c>,
/// <c>ITraceStore</c>, <c>IApprovalStore</c> and <c>IJobStore</c> are public
/// extension points with guide pages, the store guide tells the author to
/// throw an <see cref="OperationCanceledException"/>, and a store written
/// over HTTP surfaces <c>HttpClient.Timeout</c> as
/// <c>TaskCanceledException</c>. Under the old filter that one exception:
/// </para>
/// <list type="bullet">
///   <item><description>
///   faulted a hosted service's <c>ExecuteAsync</c> and stopped the whole
///   host, because .NET's default <c>BackgroundServiceExceptionBehavior</c>
///   is <c>StopHost</c>; and
///   </description></item>
///   <item><description>
///   failed a run through a recording path whose own documentation promises
///   that a store error never stops the run.
///   </description></item>
/// </list>
/// <para>
/// The first-party SQL drivers report a command timeout as a provider
/// exception, so the built-in paths never showed either one.
/// </para>
/// </remarks>
internal static class OperationCancellation
{
    /// <summary>
    /// Says whether an exception is an ordinary failure rather than this
    /// operation's own cancellation.
    /// </summary>
    /// <param name="exception">The exception that was raised.</param>
    /// <param name="cancellationToken">The token that governs this operation.</param>
    /// <returns>
    /// <see langword="true"/> for anything except an
    /// <see cref="OperationCanceledException"/> raised while
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </returns>
    public static bool IsFailure(Exception exception, CancellationToken cancellationToken)
        => exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;
}
