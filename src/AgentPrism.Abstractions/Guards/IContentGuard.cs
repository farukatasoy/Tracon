namespace AgentPrism;

/// <summary>
/// The extension point that inspects content going to and coming from the model.
/// </summary>
/// <remarks>
/// <para>
/// A guard is <strong>opt-in</strong>: <c>AddAgentPrism()</c> alone registers no
/// <see cref="IContentGuard"/>. If no guard is registered, the inspection
/// wrapper is <strong>not added</strong> to the model pipeline, and the cost
/// is exactly zero — not even a single flag check runs.
/// </para>
/// <para>
/// Registration uses <c>TryAddEnumerable</c>; multiple guards run in sequence
/// and <strong>the strictest decision wins</strong>
/// (<see cref="ContentGuardAction.Block"/> &gt;
/// <see cref="ContentGuardAction.Mask"/> &gt; <see cref="ContentGuardAction.Allow"/>).
/// A "first decision wins" rule tied to registration order was not chosen:
/// <c>TryAddEnumerable</c> order is not guaranteed, and a security decision
/// must not change based on order.
/// </para>
/// <para>
/// A guard runs at the <strong>OUTERMOST</strong> edge of the model
/// pipeline: a blocked request never reaches the network (no money is spent),
/// and blocking does not trip the circuit breaker (repeatedly blocked
/// requests do not shut down the provider).
/// </para>
/// <para>
/// A guard is <em>a control, not an observability tool.</em> The
/// "observability must not break functionality" rule does NOT apply here:
/// if this method throws, the run fails. Content that cannot be inspected is
/// never let through.
/// </para>
/// <para>
/// The implementation is on the <strong>hot path</strong> and runs on every
/// model call. It is expected not to allocate a new string when there is no match.
/// </para>
/// </remarks>
public interface IContentGuard
{
    /// <summary>
    /// The guard's name. This name is written to the audit trail and the run event.
    /// </summary>
    string Name { get; }

    /// <summary>Inspects the content.</summary>
    /// <param name="context">The inspection context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The decision. Returns <see cref="ContentGuardResult.Allow"/> when the
    /// content passes unchanged; this path allocates nothing.
    /// </returns>
    ValueTask<ContentGuardResult> InspectAsync(
        ContentGuardContext context,
        CancellationToken cancellationToken = default);
}
