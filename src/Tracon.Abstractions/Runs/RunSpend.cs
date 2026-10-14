namespace Tracon;

/// <summary>
/// The one place a run's token cost and its reported (non-token) amount are added.
/// </summary>
/// <remarks>
/// <para>
/// A decision about money — a quota entry, a budget, a webhook amount — takes its
/// number from here, never from <see cref="RunCost.Total"/> alone. A caller that
/// reads the token total only under-reports every run that reported a non-token
/// amount, and a tenant's quota ceiling can then be passed without anyone
/// noticing. The same rule as the cache charge in <see cref="RunCost.Total"/>:
/// one function answers, and the callers do not repeat the sum.
/// </para>
/// <para>
/// Amounts in two currencies are never added. When the currencies differ, the token
/// total stands alone and the reported amount is left out; a custom
/// <see cref="IRunPricingResolver"/> is the only way to get there, since the built-in
/// one prices tokens and the reported amounts in <c>Tracon:Pricing:Currency</c>.
/// </para>
/// </remarks>
public static class RunSpend
{
    /// <summary>
    /// Adds a run's token cost and the amount it reported.
    /// </summary>
    /// <param name="cost">The run's token cost, or <see langword="null"/>.</param>
    /// <param name="nonTokenCost">The run's reported (non-token) cost, or <see langword="null"/>.</param>
    /// <returns>
    /// The sum, or <see langword="null"/> when neither side has an amount. Never zero for
    /// a run nothing could be priced for.
    /// </returns>
    public static decimal? Total(RunCost? cost, RunNonTokenCost? nonTokenCost)
        => Combine(cost?.Total(), cost?.Currency, nonTokenCost);

    /// <summary>
    /// Adds a run tree's token cost and the amount the tree reported.
    /// </summary>
    /// <param name="cost">The tree's token cost, or <see langword="null"/>.</param>
    /// <param name="nonTokenCost">The tree's reported (non-token) cost, or <see langword="null"/>.</param>
    /// <returns>
    /// The sum, or <see langword="null"/> when neither side has an amount, or when the
    /// tree's token cost is in more than one currency.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Both arguments already include the root run's own share
    /// (<see cref="RunRecord.TreeCost"/>, <see cref="RunRecord.TreeNonTokenCost"/>).
    /// </para>
    /// <para>
    /// A tree whose runs are priced in more than one currency
    /// (<see cref="RunTreeCost.CurrencyCount"/>) has no total, and a partial one would
    /// read as the whole bill. A reported amount in more than one currency is left out,
    /// the same as a reported amount in another currency than the tokens.
    /// </para>
    /// </remarks>
    public static decimal? Total(RunTreeCost? cost, RunNonTokenCost? nonTokenCost)
        => cost is { CurrencyCount: > 1 }
            ? null
            : Combine(cost?.Total(), cost?.Currency, nonTokenCost is { CurrencyCount: > 1 } ? null : nonTokenCost);

    private static decimal? Combine(decimal? tokenTotal, string? tokenCurrency, RunNonTokenCost? nonTokenCost)
    {
        if (nonTokenCost?.Amount is not { } reported)
        {
            return tokenTotal;
        }

        if (tokenTotal is not { } tokens)
        {
            return reported;
        }

        return string.Equals(tokenCurrency, nonTokenCost.Currency, StringComparison.OrdinalIgnoreCase)
            ? tokens + reported
            : tokens;
    }
}
