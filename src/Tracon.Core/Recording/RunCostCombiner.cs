namespace Tracon;

/// <summary>
/// Prices a run whose tokens were spent on more than one model: each share at
/// the model that spent it, summed into one <see cref="RunCost"/>.
/// </summary>
/// <remarks>
/// <para>
/// A run's agent turns run on the agent's model, but the context compaction
/// summarizer and the <c>aiJudge</c> loop criterion can call other models
/// (<see cref="SideChannelUsageAccumulator"/>). The tree budget prices every
/// call at its own model (<see cref="RunBudgetChatClient"/>); the run's cost
/// does the same here, so the two agree. Pricing the whole usage at the
/// agent's model billed a judge's or a summarizer's tokens at the wrong rate.
/// </para>
/// <para>
/// The rules, in order:
/// </para>
/// <list type="bullet">
/// <item><description>
/// A share whose provider and model are the agent's own is added to the agent's
/// share. When no other model is left, the result is exactly what
/// <see cref="IRunPricingResolver.Resolve"/> returns for the agent's model — the
/// rate fields included.
/// </description></item>
/// <item><description>
/// Otherwise every share is priced on its own and the four cost terms are
/// summed. The <c>*PricePerMillionTokens</c> rate fields stay
/// <see langword="null"/>: one rate cannot describe a total that several rates
/// produced, and <c>runs.model_id</c> names only the agent's model.
/// </description></item>
/// <item><description>
/// A share whose model has no price adds nothing: its cost is unknown, not zero,
/// and no price is invented for it (the per-rate rule of
/// <see cref="RunPricingResolver"/>). <see cref="PricingSource.Unknown"/> is the
/// result only when NO share is priced. Otherwise the source is the agent's
/// share's, or the first priced share's when the agent's own model has no price.
/// </description></item>
/// <item><description>
/// Shares priced in different currencies are not summed: the result is
/// <see cref="PricingSource.Unknown"/>. Only a custom
/// <see cref="IRunPricingResolver"/> can produce this; the built-in one prices
/// every model in <c>Tracon:Pricing:Currency</c>.
/// </description></item>
/// </list>
/// </remarks>
internal static class RunCostCombiner
{
    /// <summary>Prices the agent's share and every other model's share of a run.</summary>
    /// <param name="resolver">The price resolver.</param>
    /// <param name="agentProvider">The provider of the model the agent's turns ran on.</param>
    /// <param name="agentModel">The model the agent's turns ran on.</param>
    /// <param name="agentUsage">The usage the agent's own model spent.</param>
    /// <param name="otherModels">The usage every other model spent (<see cref="SideChannelUsageAccumulator.ToOtherModelUsages"/>).</param>
    /// <returns>
    /// The run's cost, or <see langword="null"/> when nothing can be priced at
    /// all (no model and no usage) — the same contract as
    /// <see cref="IRunPricingResolver.Resolve"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="resolver"/> or <paramref name="otherModels"/> is <see langword="null"/>.</exception>
    public static RunCost? Resolve(
        IRunPricingResolver resolver,
        string? agentProvider,
        string? agentModel,
        RunUsage? agentUsage,
        IReadOnlyList<SideChannelModelUsage> otherModels)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(otherModels);

        List<SideChannelModelUsage>? distinctModels = null;

        foreach (var share in otherModels)
        {
            // A summarizer with no model of its own runs on the agent's model.
            if (IsAgentModel(share, agentProvider, agentModel))
            {
                agentUsage = RunUsageSum.Add(agentUsage, share.Usage);
            }
            else
            {
                (distinctModels ??= []).Add(share);
            }
        }

        var agentCost = resolver.Resolve(agentProvider, agentModel, agentUsage);

        if (distinctModels is null)
        {
            return agentCost;
        }

        var parts = new List<RunCost>(distinctModels.Count + 1);

        if (agentCost is not null)
        {
            parts.Add(agentCost);
        }

        foreach (var share in distinctModels)
        {
            if (resolver.Resolve(share.Provider, share.Model, share.Usage) is { } shareCost)
            {
                parts.Add(shareCost);
            }
        }

        return Combine(parts);
    }

    /// <summary>Sums the priced parts into one cost with no rate fields.</summary>
    /// <param name="parts">The parts, the agent's own model first when it has one.</param>
    /// <returns>The combined cost.</returns>
    /// <remarks>
    /// Sums the SAME four terms as <see cref="RunCost.Total"/>. A fifth cost
    /// term on <see cref="RunCost"/> must be added here too, or every run priced
    /// at two models under-reports it; <c>RunCostCombinerTests</c> fails when it
    /// is not.
    /// </remarks>
    internal static RunCost Combine(IReadOnlyList<RunCost> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        RunCost? first = null;
        decimal? inputCost = null;
        decimal? outputCost = null;
        decimal? cachedInputCost = null;
        decimal? cacheWriteCost = null;

        foreach (var part in parts)
        {
            if (part.Source == PricingSource.Unknown)
            {
                continue;
            }

            if (first is null)
            {
                first = part;
            }
            else if (!string.Equals(first.Currency, part.Currency, StringComparison.OrdinalIgnoreCase))
            {
                // A sum across currencies is not an amount.
                return new RunCost { Source = PricingSource.Unknown };
            }

            inputCost = AddOrNull(inputCost, part.InputCost);
            outputCost = AddOrNull(outputCost, part.OutputCost);
            cachedInputCost = AddOrNull(cachedInputCost, part.CachedInputCost);
            cacheWriteCost = AddOrNull(cacheWriteCost, part.CacheWriteCost);
        }

        if (first is null)
        {
            return new RunCost { Source = PricingSource.Unknown };
        }

        return new RunCost
        {
            InputCost = inputCost,
            OutputCost = outputCost,
            CachedInputCost = cachedInputCost,
            CacheWriteCost = cacheWriteCost,
            Currency = first.Currency,
            Source = first.Source,
        };
    }

    private static bool IsAgentModel(SideChannelModelUsage share, string? agentProvider, string? agentModel)
        => agentProvider is not null
           && agentModel is not null
           && string.Equals(share.Provider, agentProvider, StringComparison.OrdinalIgnoreCase)
           && string.Equals(share.Model, agentModel, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Adds two optional amounts, staying <see langword="null"/> when NEITHER
    /// side has one: an unpriced term is unknown, not zero.
    /// </summary>
    private static decimal? AddOrNull(decimal? left, decimal? right)
        => left is null && right is null ? null : (left ?? 0m) + (right ?? 0m);
}
