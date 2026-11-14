using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Writes to the quota counter the share of a workflow root that someone other than its own process closed: the recovery
/// that continues it and the reconciliation that claims it.
/// </summary>
/// <remarks>
/// <para>
/// A workflow root writes its share (one run and what its function nodes reported beyond tokens) when it ends
/// (<c>WorkflowRunner.CompleteAsync</c>). A process that stopped never got there, and a closer wrote the row and nothing else, so
/// what the run had spent was in no counter. The tokens of its agent steps were written by those steps; what is left is the run
/// and the money its function nodes reported, and that is in the run's items, which are written as they are reported.
/// </para>
/// <para>
/// The share is written AFTER the row is closed, once. A closer that stops between the two loses the share and never writes it
/// twice: the counter is approximate by contract, and counting a run twice is the worse error. The process that was only silent
/// and ends its run later asks the row first (<c>WorkflowRunner</c>) and does not write it again.
/// </para>
/// <para>
/// Only a root that ran is written: a workflow run (not one of its steps, which wrote their own, and not an agent run, whose tokens
/// are not on its row). A row that wrote no event never started.
/// </para>
/// </remarks>
/// <param name="runStore">The run store.</param>
/// <param name="quota">The recorder; <see langword="null"/> when none is registered.</param>
/// <param name="currency">The currency the items add up in (<c>Tracon:Pricing:Currency</c>).</param>
/// <param name="clock">The clock.</param>
/// <param name="logger">The logger, or <see langword="null"/>.</param>
internal sealed class InterruptedRunQuotaWriter(
    IRunStore runStore,
    IQuotaRecorder? quota,
    string? currency,
    TimeProvider clock,
    ILogger? logger)
{
    /// <summary>Writes the share of a closed workflow root, if it is one that ran and the counter is on.</summary>
    /// <param name="row">The closed row as the closer knows it.</param>
    /// <param name="ran">
    /// Whether the run wrote any event before it was closed, when the closer knows. <see langword="null"/> when it does not: the
    /// claim of the reconciliation writes the closing event itself, so a run that wrote none of its own has that one only
    /// (sequence 0), and the run's last event says.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the share is written or there was none to write.</returns>
    /// <remarks>Never throws except for cancellation: the row is closed and a counter that cannot be written does not undo that.</remarks>
    public async ValueTask RecordAsync(RunRecord row, bool? ran, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (quota is null || ran == false || row.Kind != RunKind.Workflow || row.ParentRunId is not null || row.TenantId is not { } tenant)
        {
            return;
        }

        // 🚨 The tenant of the row, opened in this method's own body: the reconciliation scans every tenant and has none of
        // its own, and the reads below (the row, its items) answer for the ambient tenant.
        using var scope = AmbientTenantScope.Begin(tenant);

        try
        {
            if (ran is null && await runStore.GetLastEventSequenceAsync(row.Id, cancellationToken).ConfigureAwait(false) is not > 0)
            {
                return;
            }

            // A claim of a SQL store does not return the user: the row is read again, once, for it.
            if (row.UserId is null)
            {
                row = await runStore.GetRunAsync(row.Id, cancellationToken).ConfigureAwait(false) ?? row;
            }

            var snapshot = await NonTokenCostAccumulator.SnapshotFromItemsAsync(runStore, row.Id, currency, cancellationToken).ConfigureAwait(false);

            _ = await TreeQuota.RecordClosedRootAsync(
                quota,
                tenant,
                row,
                RunSpend.Total((RunCost?)null, snapshot),
                clock.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            logger?.LogWarning(
                exception,
                "The quota share of interrupted workflow run {RunId} could not be written; the counter stays below what the run spent.",
                row.Id);
        }
    }
}
