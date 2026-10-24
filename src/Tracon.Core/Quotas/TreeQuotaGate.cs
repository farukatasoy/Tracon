namespace Tracon;

/// <summary>
/// The check a run tree that is already running makes against its tenant's quota before it starts
/// new work: a workflow function node or an agent step.
/// </summary>
/// <remarks>
/// <para>
/// The quota used to be read at the door only (<c>QuotaGate</c>, <c>429</c>). A workflow that waits
/// for a person, or that simply runs for hours, passed a quota that was spent in its first minutes,
/// and a chain of paid steps kept starting. A tree's ceiling means that no new work starts once it is
/// reached; for the tenant's quota that was not kept.
/// </para>
/// <para>
/// <strong>Only a start is refused.</strong> A node or a model call that is running is never cut off,
/// the same rule the tree's budget keeps. The counter lags behind a running tree, because a run writes
/// its consumption when it ends; the check adds the part of the tree's spend the counter has not seen
/// (<see cref="AgentRunBudget.UnrecordedSpend"/>), so the tree's own earlier nodes count.
/// </para>
/// <para>
/// Read-only on the ambient scope: the caller passes the budget it already read from the run scope, and
/// nothing here sets an <see cref="AsyncLocal{T}"/>.
/// </para>
/// </remarks>
internal static class TreeQuotaGate
{
    /// <summary>Asks the tenant's quota whether the tree may start one more thing.</summary>
    /// <param name="budget">The tree's budget, which names the tenant's counter.</param>
    /// <param name="kind">What is about to start.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The refusal, or <see langword="null"/> when the tree may start: quotas are off or not registered,
    /// no budget names a counter, or no limit of the tenant is reached.
    /// </returns>
    /// <remarks>
    /// With no rule defined for the tenant the cost is one read of the tenant's rules; with rules, two
    /// reads (the rules, then the current periods' usage). A tree whose budget no root of ours opened has
    /// no account and pays nothing.
    /// </remarks>
    public static async ValueTask<QuotaDecision?> RefusalAsync(
        AgentRunBudget? budget,
        TreeStartKind kind,
        CancellationToken cancellationToken)
    {
        if (budget?.Account is not { Quota: { IsEnabled: true } quota } account)
        {
            return null;
        }

        var (tokens, cost) = budget.UnrecordedSpend();

        var decision = await quota
            .CheckTreeStartAsync(account, kind, tokens, cost, cancellationToken)
            .ConfigureAwait(false);

        return decision.IsAllowed ? null : decision;
    }

    /// <summary>Builds the exception that ends a workflow step the tenant's quota refused.</summary>
    /// <param name="decision">The refusal.</param>
    /// <param name="message">The message, which names what was not started and carries the decision's reason.</param>
    /// <returns>The exception, carrying the metric, the period and the limit the decision names.</returns>
    public static TraconTenantQuotaExceededException ToException(QuotaDecision decision, string message)
    {
        ArgumentNullException.ThrowIfNull(decision);

        return new TraconTenantQuotaExceededException(message)
        {
            Metric = decision.Metric,
            Period = decision.Period,
            Limit = decision.Limit,
        };
    }
}
