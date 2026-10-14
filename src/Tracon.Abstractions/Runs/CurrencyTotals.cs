namespace Tracon;

/// <summary>
/// Builds a money total over many runs: amounts in two currencies are never added.
/// </summary>
/// <remarks>
/// <para>
/// Every store builds its totals here (a run tree, a statistics window, a time series,
/// an experiment arm), so the rule is written once: when the runs behind a total carry
/// more than one currency, the total keeps no amount and no currency, and its count says
/// why. Before, the in-memory store labelled the sum with the first run's currency and
/// the SQL stores with the alphabetically largest one, so 1 USD and 1 EUR read as 2 USD.
/// </para>
/// <para>
/// A store counts the distinct currencies itself, without regard to letter case: the
/// in-memory store with an <see cref="StringComparer.OrdinalIgnoreCase"/> set, a SQL
/// store with <c>COUNT(DISTINCT UPPER(...))</c>.
/// </para>
/// </remarks>
internal static class CurrencyTotals
{
    /// <summary>Builds a tree's token cost.</summary>
    /// <param name="inputCost">The summed input cost.</param>
    /// <param name="outputCost">The summed output cost.</param>
    /// <param name="cachedInputCost">The summed cache-read cost.</param>
    /// <param name="currency">A currency the runs carry.</param>
    /// <param name="currencyCount">How many currencies the runs carry.</param>
    /// <param name="runsWithUnknownPricing">How many runs have an unknown price.</param>
    /// <returns>The total; without amounts when <paramref name="currencyCount"/> is above one.</returns>
    public static RunTreeCost Tree(
        decimal? inputCost,
        decimal? outputCost,
        decimal? cachedInputCost,
        string? currency,
        int currencyCount,
        long runsWithUnknownPricing)
        => IsMixed(currencyCount)
            ? new RunTreeCost { CurrencyCount = currencyCount, RunsWithUnknownPricing = runsWithUnknownPricing }
            : new RunTreeCost
            {
                InputCost = inputCost,
                OutputCost = outputCost,
                CachedInputCost = cachedInputCost,
                Currency = currency,
                CurrencyCount = currencyCount,
                RunsWithUnknownPricing = runsWithUnknownPricing,
            };

    /// <summary>Builds a tree's reported (non-token) total.</summary>
    /// <param name="amount">The summed amount.</param>
    /// <param name="currency">A currency the runs carry.</param>
    /// <param name="currencyCount">How many currencies the runs' amounts are in.</param>
    /// <param name="isEstimated">Whether any run's amount rests on an estimate.</param>
    /// <param name="unsummedItemCount">How many items the runs' own totals left out.</param>
    /// <returns>The total; without an amount when <paramref name="currencyCount"/> is above one.</returns>
    public static RunNonTokenCost NonToken(
        decimal? amount,
        string? currency,
        int currencyCount,
        bool isEstimated,
        int unsummedItemCount)
        => new()
        {
            Amount = IsMixed(currencyCount) ? null : amount,
            Currency = IsMixed(currencyCount) ? null : currency,
            CurrencyCount = currencyCount,
            IsEstimated = isEstimated,
            UnsummedItemCount = unsummedItemCount,
        };

    /// <summary>Whether a total's runs carry more than one currency.</summary>
    /// <param name="currencyCount">How many currencies the runs carry.</param>
    /// <returns><see langword="true"/> when the amounts must not be added.</returns>
    public static bool IsMixed(int currencyCount) => currencyCount > 1;

    /// <summary>How many currencies a single total is in: one when it names one.</summary>
    /// <param name="currency">The total's currency.</param>
    /// <returns>0 or 1.</returns>
    public static int Of(string? currency) => currency is null ? 0 : 1;
}
