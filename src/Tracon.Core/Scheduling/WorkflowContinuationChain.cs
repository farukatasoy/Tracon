namespace Tracon;

/// <summary>
/// Reads the chain of runs that continue one workflow run after interruptions.
/// </summary>
/// <remarks>
/// <para>
/// Each continuation points back at the run it continues
/// (<see cref="RunRecord.ContinuedFromRunId"/>), so the chain is walked forward with
/// <see cref="RunQuery.ContinuedFromRunId"/>, newest first at each link: if a lease was lost
/// while its owner was still alive, two continuations of one run can exist, and the newer
/// one is the work in flight.
/// </para>
/// <para>
/// Recovery decides what an interrupted job does from the chain's last run, and the
/// workflow respond endpoint decides from it whether an answer's run stopped before it
/// finished. Both read it here, so the two cannot walk the chain differently.
/// </para>
/// </remarks>
internal static class WorkflowContinuationChain
{
    /// <summary>
    /// The longest chain read. Past the configured limit the work fails anyway; the bound
    /// only keeps a corrupted chain (a cycle written by hand) from looping.
    /// </summary>
    public const int MaxLength = 128;

    /// <summary>Reads a chain from its first run.</summary>
    /// <param name="runStore">The run store.</param>
    /// <param name="headRunId">The first run of the chain.</param>
    /// <param name="tenantId">The tenant the chain must belong to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The runs, first to last; empty when the first run does not exist or belongs to another tenant.
    /// </returns>
    public static async ValueTask<List<RunRecord>> ReadAsync(
        IRunStore runStore,
        Guid headRunId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runStore);

        var head = await runStore.GetRunAsync(headRunId, cancellationToken).ConfigureAwait(false);

        if (head is null || !string.Equals(head.TenantId, tenantId, StringComparison.Ordinal))
        {
            return [];
        }

        var chain = new List<RunRecord> { head };
        var seen = new HashSet<Guid> { head.Id };

        while (chain.Count < MaxLength)
        {
            var continuations = await runStore
                .QueryRunsAsync(
                    new RunQuery
                    {
                        TenantId = tenantId,
                        ContinuedFromRunId = chain[^1].Id,
                        Kind = RunKind.Workflow,
                        Take = 1,
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            var next = continuations.Count > 0 ? continuations[0] : null;

            if (next is null || !seen.Add(next.Id))
            {
                break;
            }

            chain.Add(next);
        }

        return chain;
    }
}
