namespace Tracon;

/// <summary>
/// What a workflow run's tree spent beyond tokens, for the decisions taken when the run
/// closes: the tenant's quota entry and the run's webhook.
/// </summary>
/// <remarks>
/// <para>
/// The tree's totals come from the run store, the same source the run detail reads. The
/// workflow's OWN function nodes are the one part the run holds in memory as well, and it
/// is the part the store can lose: a write of the run's items that fails switches the run's
/// recording off, so the row's snapshot never reaches the store. What the nodes spent is
/// real money whether or not it was recorded, so the decision does not depend on the
/// store holding it: when the row has no snapshot of its own, the run's in-memory one is
/// added to what the store reports for the tree.
/// </para>
/// <para>
/// When the row does hold its snapshot, the store's tree total already includes it and
/// nothing is added, so a healthy run is never counted twice.
/// </para>
/// </remarks>
internal static class WorkflowTreeSpend
{
    /// <summary>The amount the tree reported beyond tokens, including the part the store lost.</summary>
    /// <param name="record">The workflow's row as the store holds it; <see langword="null"/> when the store cannot answer.</param>
    /// <param name="own">The workflow's own sealed snapshot, held in memory.</param>
    /// <returns>The tree's total beyond tokens, or <see langword="null"/> when nothing was reported.</returns>
    public static RunNonTokenCost? NonTokenCost(RunRecord? record, RunNonTokenCost? own)
    {
        var stored = record?.TreeNonTokenCost;

        // The row holds its own snapshot: the tree total has it. Nothing was reported:
        // there is nothing to add.
        if (own is null || record?.NonTokenCost is not null)
        {
            return stored;
        }

        if (stored is null)
        {
            return own;
        }

        // A sum across currencies is not an amount (CurrencyTotals): the tree keeps
        // no amount and counts its currencies, the same as when the store adds them.
        var differs = stored.Amount is not null && own.Amount is not null &&
                      !string.Equals(stored.Currency, own.Currency, StringComparison.OrdinalIgnoreCase);
        var currencies = differs ? Currencies(stored) + 1 : Math.Max(Currencies(stored), Currencies(own));

        return CurrencyTotals.NonToken(
            stored.Amount is null && own.Amount is null ? null : (stored.Amount ?? 0m) + (own.Amount ?? 0m),
            stored.Currency ?? own.Currency,
            currencies,
            stored.IsEstimated || own.IsEstimated,
            stored.UnsummedItemCount + own.UnsummedItemCount);
    }

    /// <summary>How many currencies a total is in, also for a snapshot built without the count.</summary>
    private static int Currencies(RunNonTokenCost cost)
        => Math.Max(cost.CurrencyCount, cost.Amount is null ? 0 : CurrencyTotals.Of(cost.Currency));
}
