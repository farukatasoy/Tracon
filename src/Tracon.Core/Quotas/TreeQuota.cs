namespace Tracon;

/// <summary>
/// The one path by which a run in a call tree, or a tool call that outlives its run,
/// adds its consumption to the tree's quota counter.
/// </summary>
/// <remarks>
/// <para>
/// A gate, not a convenience. Four writers add to the same counter — the agent root,
/// a child agent run, the workflow root and a tool call that ends after its run — and
/// each one that builds its own <see cref="QuotaConsumption"/> is a place the next term
/// (the next thing a run spends) can be added in three of them and forgotten in the
/// fourth, which is how a charge goes missing from a tenant's ceiling. The identity is
/// also here once: the root's tenant, name and user, from the tree's
/// <see cref="TreeAccount"/>, never from whoever happens to be calling.
/// </para>
/// <para>
/// <c>grep -rn "new QuotaConsumption" src/</c> shows this file and nothing else. It is also the only
/// place that credits the tree's ledger with what goes to the counter (<see cref="TreeLedger"/>), and it does so before the store write.
/// </para>
/// </remarks>
internal static class TreeQuota
{
    /// <summary>Adds one entry to the counter the account names.</summary>
    /// <param name="account">The tree's account, written by its root.</param>
    /// <param name="runId">The run that spent: the one a threshold webhook names.</param>
    /// <param name="runs">The runs to add: 1 for a root, 0 for everything that spends under it.</param>
    /// <param name="tokens">The tokens to add.</param>
    /// <param name="amount">
    /// The amount to add, or <see langword="null"/> when nothing was priced — not zero,
    /// so a monetary rule falls back to tokens instead of reading an unpriced run as free.
    /// </param>
    /// <param name="occurredAt">When the consumption was settled; the period is computed from it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The thresholds the entry newly crossed, for the caller to write into a stream that
    /// is still open; empty when nothing was written.
    /// </returns>
    /// <remarks>
    /// An entry that adds nothing is not written (no run counted, no token, no amount):
    /// it would cost the store a round trip and the threshold check two more, and the
    /// counter would not change. Never throws except for cancellation:
    /// <see cref="IQuotaRecorder.RecordAsync"/> keeps the contract that a counter which
    /// cannot be written does not fail a run.
    /// </remarks>
    internal static async ValueTask<IReadOnlyList<QuotaThresholdCrossing>> RecordAsync(
        TreeAccount account,
        Guid runId,
        int runs,
        long tokens,
        decimal? amount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        // The one gate every writer goes through, so a term added to what a run
        // spends is added once. The identity is the tree's, read from the account,
        // because a child that ends after its root carries neither the root's tenant nor user.
        if (account.Quota is not { } quota || (runs <= 0 && tokens <= 0 && amount is null or <= 0m))
        {
            return [];
        }

        // The ledger is the other side of the difference the budget's live meter makes with the
        // counter, and it is written here and nowhere else. It takes the entry BEFORE the store does: a
        // check at the start of new work reads the counter first and the tree's unwritten spend after it
        // (QuotaEnforcer.CheckTreeStartAsync), so an entry the counter holds is never also counted as
        // unwritten. Written after the store, the entry was in both while the write and the threshold
        // claims ran, and a background child that started while its root closed was refused at 20 of 15
        // tokens on a tree that had spent 10. A check that reads between the two sees the entry in neither:
        // the quota then errs the way it is documented to (approximate, it may let a start through near its
        // limit), not by refusing a start it allows.
        // A counter that could not be written does not fail a run (RecordAsync keeps that contract) and is
        // not told apart from one that could: the entry is lost to the counter, not owed to it.
        account.Ledger.Record(tokens, amount);

        try
        {
            return await quota.RecordAsync(
                new QuotaConsumption
                {
                    TenantId = account.TenantId,
                    AgentName = account.RootName,
                    Runs = runs,
                    Tokens = tokens,
                    Cost = amount,
                    OccurredAt = occurredAt,
                    RunId = runId.ToString(),
                    UserId = account.UserId,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The caller stopped the write, so the counter may not have the entry: the tree still owes it,
            // as it did when the ledger was written after the store.
            account.Ledger.Withdraw(tokens, amount);

            throw;
        }
    }
}
