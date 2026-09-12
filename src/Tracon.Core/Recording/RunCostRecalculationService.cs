namespace Tracon;

/// <summary>
/// Maintenance service that fills in the cost of runs whose price is still
/// unknown, against the current pricing source (catalog/configuration).
/// </summary>
/// <remarks>
/// Used only by the <c>POST /api/stats/recalculate-costs</c> endpoint. A run's
/// cost is a price snapshot (<see cref="RunCost"/>): a run that already
/// carries a known price (<see cref="PricingSource.Catalog"/> or
/// <see cref="PricingSource.Configuration"/>) is <strong>never</strong>
/// rewritten, even if the price list changed since. Only
/// <see cref="PricingSource.Unknown"/> rows — and rows with no cost at all,
/// from before cost tracking existed — are candidates. A row written before
/// <c>runs.model_provider</c> existed resolves by model name alone, the same
/// as before this field existed; a row written after carries its own provider
/// and resolves against it directly.
/// </remarks>
internal sealed class RunCostRecalculationService
{
    private const int PageSize = 200;

    private readonly IRunStore _store;
    private readonly IRunPricingResolver _resolver;

    /// <summary>Creates a new recalculation service.</summary>
    /// <param name="store">The run store.</param>
    /// <param name="resolver">The cost resolver.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public RunCostRecalculationService(IRunStore store, IRunPricingResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(resolver);

        _store = store;
        _resolver = resolver;
    }

    /// <summary>Recalculates the cost of all runs for a tenant.</summary>
    /// <param name="tenantId">The tenant identity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The counts of runs considered, updated, and still unknown.</returns>
    public async ValueTask<RunCostRecalculationResult> RecalculateAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        long considered = 0;
        long updated = 0;
        long stillUnknown = 0;
        long skipped = 0;

        for (var skip = 0; ; skip += PageSize)
        {
            var page = await _store.QueryRunsAsync(
                new RunQuery
                {
                    TenantId = tenantId,
                    OnlyRootRuns = false,
                    Skip = skip,
                    Take = PageSize,
                },
                cancellationToken).ConfigureAwait(false);

            if (page.Count == 0)
            {
                break;
            }

            foreach (var run in page)
            {
                if (run.ModelId is not { Length: > 0 } || run.Usage is null)
                {
                    continue;
                }

                // 🚨 A priced run's cost is a SNAPSHOT and is never rewritten — the
                // endpoint's job is to fill in a gap, not to re-apply today's
                // pricing to history. A row with no Cost at all predates cost
                // tracking and is treated the same as Unknown: there is nothing
                // to protect.
                if (run.Cost is { Source: not PricingSource.Unknown })
                {
                    skipped++;
                    continue;
                }

                considered++;

                var cost = _resolver.Resolve(run.ModelProvider, run.ModelId, run.Usage);
                if (cost is null)
                {
                    continue;
                }

                // The identities came from a query already FILTERED by tenant; we
                // carry the same tenant into the write as well (K-355). On this
                // two-step path, a record cannot switch to a different tenant in between.
                await _store.UpdateRunCostAsync(run.Id, cost, run.TenantId, cancellationToken)
                    .ConfigureAwait(false);

                if (cost.Source == PricingSource.Unknown)
                {
                    stillUnknown++;
                }
                else
                {
                    updated++;
                }
            }

            if (page.Count < PageSize)
            {
                break;
            }
        }

        return new RunCostRecalculationResult
        {
            RunsConsidered = considered,
            RunsUpdated = updated,
            RunsStillUnknown = stillUnknown,
            RunsSkipped = skipped,
        };
    }
}
