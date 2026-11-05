namespace Tracon;

/// <summary>
/// Walks the exception graph of a provider failure the way the code that DECIDES reads it: whole, with the exception
/// the provider client really threw, never the masked view a writer is shown.
/// </summary>
/// <remarks>
/// <para>
/// A failure has two readers. A writer (a logger, a span, an error tracker) takes text from it, and what it reaches
/// through <see cref="Exception.InnerException"/> is a <see cref="MaskedFailureView"/> with the call's credential
/// replaced. A decision (the fallback chain, the circuit breaker, the retry of a workflow node, the job worker) takes
/// facts from it that a view cannot carry: the type of a frame, <see cref="HttpRequestException.StatusCode"/> (a
/// property, not text), a cancellation or a <see cref="TimeoutException"/> buried in an <see cref="AggregateException"/>.
/// Every decision reads through this class, so every one of them gets the same answer from a masked failure as from
/// a failure with nothing to mask.
/// </para>
/// <para>
/// There is one descent rule and it lives here. A walk written by hand with <see cref="Exception.InnerException"/>
/// stops at the view and decides from less than the graph holds, silently: nothing fails to compile and the unit test
/// of a failure with nothing to mask stays green.
/// </para>
/// </remarks>
internal static class ProviderFailureGraph
{
    /// <summary>Yields <paramref name="exception"/> and every exception reachable through it, parent before children.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns>The frames, in the order a reader meets them.</returns>
    internal static IEnumerable<Exception> Frames(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var pending = new Stack<Exception>();

        pending.Push(exception);

        while (pending.Count > 0)
        {
            var frame = pending.Pop();

            yield return frame;

            var children = Children(frame);

            for (var index = children.Count - 1; index >= 0; index--)
            {
                pending.Push(children[index]);
            }
        }
    }

    /// <summary>Gets the exceptions one frame holds, the way a decision reads them.</summary>
    /// <param name="frame">One exception of a graph.</param>
    /// <returns>
    /// The branches of an <see cref="AggregateException"/>; for a frame Tracon tagged or normalized, the exception the
    /// provider client threw; for any other frame, its inner exception.
    /// </returns>
    internal static IReadOnlyList<Exception> Children(Exception frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        return frame switch
        {
            AggregateException aggregate => aggregate.InnerExceptions,
            ForeignProviderInvocationException foreign => [foreign.Original],
            ProviderInvocationException invocation => [invocation.Original],
            _ => frame.InnerException is { } inner ? [inner] : [],
        };
    }

    /// <summary>Gets the exception a consumer's retry classifier is asked about.</summary>
    /// <param name="exception">The failure a link of the fallback chain threw.</param>
    /// <returns>
    /// For a failure Tracon tagged or normalized: the same failure with the message and the text masked and the
    /// provider client's own exception as the inner exception, the shape that classifier has always been handed.
    /// Any other failure, as it is.
    /// </returns>
    /// <remarks>
    /// A classifier is consumer code: it matches the type of an SDK exception, and it walks
    /// <see cref="Exception.InnerException"/> to find one. A view would hide that type from it. What it writes is its
    /// own business; the text it can reach through <see cref="Exception.Message"/> and <see cref="object.ToString"/>
    /// is masked.
    /// </remarks>
    internal static Exception DecisionView(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            ForeignProviderInvocationException foreign => foreign.ForDecision(),
            ProviderInvocationException invocation => invocation.ForDecision(),
            _ => exception,
        };
    }
}
