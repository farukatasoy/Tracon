using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Reads what the earlier runs of one logical workflow run already spent, for the budget of the run that
/// continues them.
/// </summary>
/// <remarks>
/// <para>
/// A workflow that waits for a person, is resumed by an operator or is picked up after an interruption is
/// one run to its owner, and each step of that chain is a run row with a budget of its own. The ceiling
/// (<c>Tracon:AgentGraph</c>) is the logical run's. A continuation that started from zero gave every
/// answer of a workflow with a human approval loop the whole ceiling again, and a run that failed for its
/// budget ran the node that was refused when it was resumed.
/// </para>
/// <para>
/// <strong>The chain is read backward from the run being continued.</strong> A run's predecessor is the run
/// it names as the one it continues (<see cref="RunRecord.ContinuedFromRunId"/>: a recovery, an operator's
/// resume), or, for a run that answered a person, the run its
/// <see cref="RunEventType.WorkflowResponseApplied"/> event names. The chain is read once, when the
/// continuation begins; nothing is read per node.
/// </para>
/// <para>
/// <strong>Fail closed.</strong> A chain that cannot be read does not start the run with a budget of zero:
/// a silent zero is the hole this class closes. The reader throws, and the run ends <c>Failed</c> before
/// any node starts. A predecessor the store no longer holds (retention removed it) ends the chain; what it
/// spent is gone and the log says so.
/// </para>
/// <para>
/// The totals come from each row's tree columns (<see cref="RunRecord.TreeUsage"/>,
/// <see cref="RunRecord.TreeCost"/>, <see cref="RunRecord.TreeNonTokenCost"/>), added by
/// <see cref="RunSpend"/> and nowhere else. A child run still running when its workflow row was read
/// is not in them, and the child count is the direct children only: a nested sub-agent call is spent, but
/// not counted as a started run.
/// </para>
/// <para>
/// <strong>A run an interruption closed holds no snapshot of what its function nodes reported</strong>: the process
/// that kept the total was gone, and the recovery closes the row without one. The amounts are in the run's items,
/// which were written as they were reported, and the lineage adds them up with the rule the run itself would have
/// (<see cref="NonTokenCostAccumulator"/>), through the same step the run's own close takes
/// (<see cref="WorkflowTreeSpend"/>). Without it the chain a crash left would start again with the money its
/// function nodes had spent not counted.
/// </para>
/// </remarks>
internal static class BudgetLineage
{
    /// <summary>
    /// The longest chain read, the bound the recovery chain keeps
    /// (<see cref="WorkflowContinuationChain.MaxLength"/>).
    /// </summary>
    public const int MaxLength = WorkflowContinuationChain.MaxLength;

    // The error type a run carries when the reconciliation claimed it after its process stopped (the run store
    // contracts pin the value; the recovery's own is WorkflowRecovery.InterruptedErrorType).
    private const string OrphanedErrorType = "orphaned";

    // The link is written before the graph does anything (WorkflowRunner.RunGuardedAsync), right after
    // the first event; a few events are enough to find it and a long history is never read for it.
    private const int LinkScanEvents = 4;

    /// <summary>Reads what the chain that ends at <paramref name="runId"/> spent.</summary>
    /// <param name="runStore">The run store.</param>
    /// <param name="runId">The last run of the chain: the one the new run continues.</param>
    /// <param name="tenantId">The tenant every run of the chain must belong to.</param>
    /// <param name="currency">The currency Tracon prices in (<c>Tracon:Pricing:Currency</c>), the one a run's items add up in.</param>
    /// <param name="timeProvider">The time source, for a run that has not closed yet.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the chain spent and how long its runs were executing.</returns>
    /// <exception cref="TraconException">
    /// The chain could not be read, or it is longer than <see cref="MaxLength"/>.
    /// </exception>
    public static async ValueTask<BudgetCarryOver> ReadAsync(
        IRunStore runStore,
        Guid runId,
        string tenantId,
        string? currency,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        var tokens = 0L;
        var cost = 0m;
        var runs = 0;
        var active = TimeSpan.Zero;
        var seen = new HashSet<Guid>();

        try
        {
            Guid? cursor = runId;

            while (cursor is { } id)
            {
                if (!seen.Add(id))
                {
                    logger.LogWarning(
                        "The runs that continue one another loop back to run {RunId}; the budget counts each run once.",
                        id);

                    break;
                }

                if (seen.Count > MaxLength)
                {
                    throw new TraconException(
                        $"The runs this run continues form a chain longer than {MaxLength}, so what they spent was not read " +
                        "and the run does not start.");
                }

                var run = await runStore.GetRunAsync(id, cancellationToken).ConfigureAwait(false);

                if (run is null || !string.Equals(run.TenantId, tenantId, StringComparison.Ordinal))
                {
                    // The head is the run the caller named and exists. A link that points nowhere is a
                    // predecessor retention removed, or another tenant's row that no tenant may read.
                    logger.LogWarning(
                        "Run {RunId}, which an earlier run of the chain continues, was not found; what it spent is not counted.",
                        id);

                    break;
                }

                tokens = AddSaturating(tokens, run.TreeUsage?.TotalTokens ?? 0);
                cost += RunSpend.Total(
                            run.TreeCost,
                            WorkflowTreeSpend.NonTokenCost(
                                run,
                                run.NonTokenCost is null
                                    ? await SnapshotFromItemsAsync(runStore, run.Id, currency, cancellationToken).ConfigureAwait(false)
                                    : null)) ?? 0m;
                runs += run.ChildRunCount;
                active += await ActiveTimeAsync(runStore, run, timeProvider, logger, cancellationToken).ConfigureAwait(false);

                cursor = run.ContinuedFromRunId ?? await ReadAnsweredRunAsync(runStore, run.Id, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken) &&
                                           exception is not TraconException)
        {
            throw new TraconException(
                $"What the runs that run '{runId}' continues spent could not be read, so the run does not start " +
                "with a budget of zero. Start the continuation again once the run store answers.",
                exception);
        }

        return new BudgetCarryOver(tokens, cost, runs, active);
    }

    /// <summary>
    /// What a run's own function nodes reported, from the items they were written to, for a row that holds no snapshot.
    /// </summary>
    /// <returns>The snapshot the run would have closed with; <see langword="null"/> when it reported nothing.</returns>
    private static async ValueTask<RunNonTokenCost?> SnapshotFromItemsAsync(
        IRunStore runStore,
        Guid runId,
        string? currency,
        CancellationToken cancellationToken)
    {
        var items = await runStore.ListNonTokenUsageAsync(runId, cancellationToken).ConfigureAwait(false);

        if (items.Count == 0)
        {
            return null;
        }

        var accumulator = new NonTokenCostAccumulator(currency);

        foreach (var item in items)
        {
            accumulator.AddNodeItem(item.Usage);
        }

        return accumulator.Seal();
    }

    /// <summary>
    /// Finds the run a run answered: the run that waited for the person.
    /// </summary>
    /// <param name="runStore">The run store.</param>
    /// <param name="runId">The run that may be an answer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The run that waited, or <see langword="null"/> when this run answers nobody.</returns>
    private static async ValueTask<Guid?> ReadAnsweredRunAsync(IRunStore runStore, Guid runId, CancellationToken cancellationToken)
    {
        var scanned = 0;

        await foreach (var runEvent in runStore.ReadEventsAsync(runId, 0, cancellationToken).ConfigureAwait(false))
        {
            if (runEvent.Type == RunEventType.WorkflowResponseApplied)
            {
                return WorkflowResponseLink.ReadSourceRunId(runEvent.Payload);
            }

            if (++scanned >= LinkScanEvents)
            {
                break;
            }
        }

        return null;
    }

    /// <summary>How long a run was executing: from its start to the moment it stopped doing work.</summary>
    /// <remarks>
    /// <para>
    /// A run that waited for a person closed when it began to wait, so the wait is not in it. A run that is
    /// still open (the operator resumed a run that had not closed yet) is counted up to now.
    /// </para>
    /// <para>
    /// <strong>A run that someone else closed after its process died</strong> (an interruption the recovery closed, an
    /// orphan the reconciliation claimed) carries the moment of the closing, which can be hours after the process stopped:
    /// the host was down, the lease had to expire. That wait is not execution. For these rows the end is the last thing
    /// the run itself wrote, and the closing event is not that. A run that did nothing for a long time before it died
    /// (one slow tool call) is counted short by that time; the alternative is counting the outage, which refuses a
    /// continuation for the time its host was down.
    /// </para>
    /// </remarks>
    private static async ValueTask<TimeSpan> ActiveTimeAsync(
        IRunStore runStore,
        RunRecord run,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var end = run.CompletedAt;

        if (end is null)
        {
            logger.LogWarning(
                "Run {RunId}, which a later run continues, has not closed ({Status}); its time is counted up to now.",
                run.Id,
                run.Status);

            end = timeProvider.GetUtcNow();
        }
        else if (run.Error?.Type is WorkflowRecovery.InterruptedErrorType or OrphanedErrorType)
        {
            end = await LastOwnWriteAsync(runStore, run, cancellationToken).ConfigureAwait(false) ?? end;
        }

        var elapsed = end.Value - run.StartedAt;

        return elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero;
    }

    /// <summary>When the run last wrote an event itself: the last event, unless the last one is the closing event someone else wrote.</summary>
    /// <returns>The time, or <see langword="null"/> when the run wrote nothing.</returns>
    private static async ValueTask<DateTimeOffset?> LastOwnWriteAsync(IRunStore runStore, RunRecord run, CancellationToken cancellationToken)
    {
        DateTimeOffset? beforeLast = null;
        RunEvent? last = null;

        await foreach (var runEvent in runStore.ReadEventsAsync(run.Id, 0, cancellationToken).ConfigureAwait(false))
        {
            if (last is not null && (beforeLast is null || last.Timestamp > beforeLast))
            {
                beforeLast = last.Timestamp;
            }

            last = runEvent;
        }

        // The closer writes one RunFailed event as the run's last line; anything else last is the run's own. A run
        // whose only event is the closing one did nothing. A run with no events at all (the store lost them) says nothing.
        return last is null
            ? null
            : last.Type == RunEventType.RunFailed ? beforeLast ?? run.StartedAt : last.Timestamp;
    }

    private static long AddSaturating(long total, long amount)
        => amount > 0 && long.MaxValue - total < amount ? long.MaxValue : total + Math.Max(amount, 0);
}
