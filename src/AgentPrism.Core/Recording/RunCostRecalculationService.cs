namespace AgentPrism;

/// <summary>
/// Maintenance service that recalculates the cost of all runs against the
/// current pricing source (catalog/configuration).
/// </summary>
/// <remarks>
/// Used only by the <c>POST /api/stats/recalculate-costs</c> endpoint. Every
/// call is a <strong>full</strong> recalculation — there is no "only unknown
/// ones" filter, because the endpoint's purpose is to apply the current
/// pricing to history as-is. Since the provider is not stored on historical
/// rows, resolution is done by model name alone (see
/// <see cref="IRunPricingResolver.Resolve"/> and <c>docs/KARARLAR.md</c> K-154).
/// </remarks>
public sealed class RunCostRecalculationService
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

                considered++;

                var cost = _resolver.Resolve(provider: null, run.ModelId, run.Usage);
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
        };
    }
}
