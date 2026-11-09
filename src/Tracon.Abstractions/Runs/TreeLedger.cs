namespace Tracon;

/// <summary>
/// What a call tree has credited to its quota counter so far, and the spend its budget held when
/// the counter was named.
/// </summary>
/// <remarks>
/// <para>
/// A run writes its own consumption when it ends, so while a tree runs the counter lags behind
/// what the tree has spent. The budget is the live meter; this is the other side of the
/// difference. <c>AgentRunBudget.UnrecordedSpend</c> is the budget's consumption, less the
/// baseline, less what was credited: the part of the tree's spend the counter has not seen. A
/// quota check at the start of a node or an agent step adds it to the counter's value.
/// </para>
/// <para>
/// The baseline is the spend the budget already held when the tree's root named its account:
/// the earlier runs of a continued workflow, which wrote their own share when they ended.
/// Counted again it would be counted twice.
/// </para>
/// <para>
/// The only writer of the recorded side is <c>TreeQuota.RecordAsync</c>, the one path every
/// quota entry goes through.
/// </para>
/// </remarks>
internal sealed class TreeLedger
{
    private long _recordedTokens;
    private long _recordedCostNanoUnits;
    private long _baselineTokens;
    private long _baselineCostNanoUnits;

    /// <summary>Gets the tokens credited to the counter.</summary>
    public long RecordedTokens => Interlocked.Read(ref _recordedTokens);

    /// <summary>Gets the cost credited to the counter, in nano-units.</summary>
    public long RecordedCostNanoUnits => Interlocked.Read(ref _recordedCostNanoUnits);

    /// <summary>Gets the tokens the budget held when the account was named.</summary>
    public long BaselineTokens => Interlocked.Read(ref _baselineTokens);

    /// <summary>Gets the cost the budget held when the account was named, in nano-units.</summary>
    public long BaselineCostNanoUnits => Interlocked.Read(ref _baselineCostNanoUnits);

    /// <summary>Names the spend the budget already holds, which this tree does not write.</summary>
    /// <param name="tokens">The tokens the budget holds.</param>
    /// <param name="costNanoUnits">The cost the budget holds, in nano-units.</param>
    public void Rebase(long tokens, long costNanoUnits)
    {
        Interlocked.Exchange(ref _baselineTokens, tokens);
        Interlocked.Exchange(ref _baselineCostNanoUnits, costNanoUnits);
    }

    /// <summary>
    /// Adds one entry the tree is about to write to the counter. It is called before the store write, not after,
    /// and <see cref="Withdraw"/> takes the entry back when the caller stops that write.
    /// </summary>
    /// <param name="tokens">The tokens of the entry.</param>
    /// <param name="amount">The amount of the entry, or <see langword="null"/> when none was.</param>
    public void Record(long tokens, decimal? amount)
    {
        if (tokens > 0)
        {
            AddSaturating(ref _recordedTokens, tokens);
        }

        if (amount is > 0m)
        {
            AddSaturating(ref _recordedCostNanoUnits, AgentRunBudget.ToNanoUnits(amount.Value));
        }
    }

    /// <summary>Takes back an entry <see cref="Record"/> added, when the write it stood for was stopped.</summary>
    /// <param name="tokens">The tokens the entry added.</param>
    /// <param name="amount">The amount the entry added, or <see langword="null"/> when none was.</param>
    public void Withdraw(long tokens, decimal? amount)
    {
        if (tokens > 0)
        {
            Interlocked.Add(ref _recordedTokens, -tokens);
        }

        if (amount is > 0m)
        {
            Interlocked.Add(ref _recordedCostNanoUnits, -AgentRunBudget.ToNanoUnits(amount.Value));
        }
    }

    // The same rule as the budget's own counters: a total that wraps would turn a large
    // difference into a small one.
    private static void AddSaturating(ref long location, long amount)
    {
        var current = Interlocked.Read(ref location);

        while (true)
        {
            var next = long.MaxValue - current < amount ? long.MaxValue : current + amount;
            var observed = Interlocked.CompareExchange(ref location, next, current);

            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }
}
