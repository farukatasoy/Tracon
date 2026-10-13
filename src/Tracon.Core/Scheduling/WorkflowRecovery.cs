using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Finds the run a queued workflow job was running when its process stopped, and
/// decides what the job does with it when it is leased again.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The job is the owner (F-321).</strong> Run reconciliation still closes an
/// orphaned workflow root as <see cref="RunStatus.Failed"/>, but it never resumes
/// one: two owners would resume the same run twice. Only the job that started the
/// run continues it, when it is leased again.
/// </para>
/// <para>
/// <strong>The chain.</strong> The job knows the FIRST run of the work (the id it
/// wrote on its item, or the queued run's id). Each continuation points back at the
/// run it continues (<see cref="RunRecord.ContinuedFromRunId"/>), so the chain is
/// walked forward with <see cref="RunQuery.ContinuedFromRunId"/>, newest first at
/// each link, and its last run decides:
/// </para>
/// <list type="bullet">
/// <item>No row, or the first run still <see cref="RunStatus.Queued"/>: the work never started.</item>
/// <item><see cref="RunStatus.Completed"/> or <see cref="RunStatus.AwaitingInput"/>: the work is done.</item>
/// <item>
/// A continuation still <see cref="RunStatus.Queued"/>: it was opened and never
/// started; it is started now, under the same id, and does not count again.
/// </item>
/// <item>
/// Any other status: the run was interrupted. Within
/// <see cref="IWorkflowRecoveryPolicy.MaxResumeAttempts"/> continuations a new run
/// continues it; past it, the work fails.
/// </item>
/// </list>
/// <para>
/// A crash, a deploy and a graceful shutdown are not told apart: a crash leaves the
/// run <see cref="RunStatus.Running"/>, a graceful shutdown closes it
/// <see cref="RunStatus.Canceled"/>, and reconciliation may have closed an orphan
/// <see cref="RunStatus.Failed"/> in the meantime. The handler reports an item as
/// soon as its run ends, so a run that failed or was cancelled on its own does not
/// reach this path.
/// </para>
/// <para>
/// <strong>The checkpoint.</strong> The continuation resumes from the latest
/// checkpoint in the chain, searched from the last run backward. A chain with no
/// checkpoint at all (the process stopped before the first super-step ended) has
/// nothing to resume from: the caller starts the operation again.
/// </para>
/// <para>
/// Every store read here takes the job's tenant explicitly; the worker opens the
/// ambient tenant scope too, for the store calls that read it.
/// </para>
/// </remarks>
/// <param name="runStore">The run store.</param>
/// <param name="checkpointStore">The checkpoint store.</param>
/// <param name="policy">The recovery settings; <see langword="null"/> when the workflow engine is not registered.</param>
/// <param name="clock">The time source.</param>
/// <param name="logger">The logger.</param>
internal sealed class WorkflowRecovery(
    IRunStore runStore,
    IWorkflowCheckpointStore checkpointStore,
    IWorkflowRecoveryPolicy? policy,
    TimeProvider? clock = null,
    ILogger? logger = null)
{
    /// <summary>The error type an interrupted run is closed with.</summary>
    public const string InterruptedErrorType = "interrupted";

    /// <summary>
    /// The longest chain read. Past the configured limit the work fails anyway; the
    /// bound only keeps a corrupted chain (a cycle written by hand) from looping.
    /// </summary>
    private const int MaxChainLength = 128;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>Gets whether recovery is on.</summary>
    public bool IsOn => WorkflowRecoveryPolicy.IsOn(policy);

    /// <summary>Decides what to do with the work that starts with <paramref name="headRunId"/>.</summary>
    /// <param name="headRunId">The first run of the work.</param>
    /// <param name="tenantId">The job's tenant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The plan.</returns>
    public async ValueTask<WorkflowRecoveryPlan> PlanAsync(
        Guid headRunId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var chain = await ReadChainAsync(headRunId, tenantId, cancellationToken).ConfigureAwait(false);

        if (chain.Count == 0 || (chain.Count == 1 && chain[0].Status == RunStatus.Queued))
        {
            return new WorkflowRecoveryPlan { Step = WorkflowRecoveryStep.NotStarted, Chain = chain };
        }

        var last = chain[^1];

        if (last.Status is RunStatus.Completed or RunStatus.AwaitingInput)
        {
            return new WorkflowRecoveryPlan { Step = WorkflowRecoveryStep.Settled, Chain = chain, Last = last };
        }

        if (last.Status == RunStatus.Queued)
        {
            // A continuation opened and never started: its row is the marker.
            // It already counted when it was opened.
            var earlier = chain.GetRange(0, chain.Count - 1);

            return new WorkflowRecoveryPlan
            {
                Step = WorkflowRecoveryStep.Continue,
                Chain = chain,
                Last = chain[^2],
                ContinuationRunId = last.Id,
                ContinuationIsOpen = true,
                Checkpoint = await FindCheckpointAsync(earlier, tenantId, cancellationToken).ConfigureAwait(false),
            };
        }

        var continuations = chain.Count - 1;

        if (continuations >= (policy?.MaxResumeAttempts ?? 0))
        {
            return new WorkflowRecoveryPlan { Step = WorkflowRecoveryStep.Exhausted, Chain = chain, Last = last };
        }

        return new WorkflowRecoveryPlan
        {
            Step = WorkflowRecoveryStep.Continue,
            Chain = chain,
            Last = last,
            ContinuationRunId = TraconId.NewId(),
            Checkpoint = await FindCheckpointAsync(chain, tenantId, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>
    /// Closes an interrupted run that is still <see cref="RunStatus.Running"/> as
    /// <see cref="RunStatus.Failed"/>, naming what happens next.
    /// </summary>
    /// <param name="run">The interrupted run.</param>
    /// <param name="continuationRunId">The run that continues it; <see langword="null"/> when none does.</param>
    /// <param name="continuations">How many continuations the chain already has, named when no run continues it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the row is closed.</returns>
    /// <remarks>
    /// The process that was writing the run is gone, so this writes the closing
    /// event itself, one past the last sequence, as run reconciliation does. A run
    /// in another status keeps the error it was closed with.
    /// </remarks>
    public async ValueTask CloseInterruptedAsync(
        RunRecord run,
        Guid? continuationRunId,
        int continuations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.Status != RunStatus.Running)
        {
            return;
        }

        var message = continuationRunId is { } next
            ? $"The process running this workflow run stopped before the run ended. Run '{next}' continues it from its latest checkpoint."
            : "The process running this workflow run stopped before the run ended. It is not continued: " +
              $"its chain already has {continuations} continuation run(s), and 'Tracon:Workflows:MaxResumeAttempts' allows no more.";

        var now = _clock.GetUtcNow();
        var last = await runStore.GetLastEventSequenceAsync(run.Id, cancellationToken).ConfigureAwait(false);
        var sequence = (last ?? -1) + 1;

        try
        {
            await runStore.AppendEventAsync(
                new RunEvent
                {
                    RunId = run.Id,
                    Sequence = sequence,
                    Type = RunEventType.RunFailed,
                    Timestamp = now,
                    Text = message,
                    TenantId = run.TenantId,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            // The closing event is the record's last line, not its status. A
            // lost write (the old owner still alive and writing, K-886) must
            // not stop the row from closing.
            logger?.LogWarning(exception, "Could not write the closing event of interrupted workflow run {RunId}.", run.Id);
        }

        await runStore.CompleteRunAsync(
            new RunCompletion
            {
                RunId = run.Id,
                Status = RunStatus.Failed,
                CompletedAt = now,
                EventCount = sequence + 1,
                Error = new RunError
                {
                    Type = InterruptedErrorType,
                    Message = message,
                    Class = RunErrorClass.Infrastructure,

                    // A fixed fingerprint, like the orphaned run's: every
                    // interruption is the same failure, whatever its message names.
                    Fingerprint = InterruptedErrorType,
                },
                TenantId = run.TenantId,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the continuation's row before the engine starts it, with the link to
    /// the run it continues.
    /// </summary>
    /// <param name="plan">A <see cref="WorkflowRecoveryStep.Continue"/> plan.</param>
    /// <param name="userId">The user the job was queued by, or <see langword="null"/>.</param>
    /// <param name="labels">The labels the job was queued with, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the row is written.</returns>
    /// <remarks>
    /// The engine writes the row again when it starts the run and does not know the
    /// link; the store keeps the one written here (<see cref="RunStartInfo.ContinuedFromRunId"/>).
    /// The row is <see cref="RunStatus.Queued"/> until then, so a job leased again
    /// before the engine started finds it and starts it under the same id.
    /// </remarks>
    public async ValueTask OpenContinuationAsync(
        WorkflowRecoveryPlan plan,
        string? userId,
        IReadOnlyDictionary<string, string>? labels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.ContinuationIsOpen || plan.ContinuationRunId is not { } runId || plan.Last is not { } last)
        {
            return;
        }

        var workflowName = last.WorkflowName ?? last.AgentName;

        await runStore.StartRunAsync(
            new RunStartInfo
            {
                RunId = runId,
                AgentName = workflowName,
                Kind = RunKind.Workflow,
                WorkflowName = workflowName,
                Status = RunStatus.Queued,
                StartedAt = _clock.GetUtcNow(),
                TenantId = last.TenantId,
                UserId = userId,
                Labels = labels,
                SessionId = plan.SessionId,
                IsStreaming = true,
                ContinuedFromRunId = last.Id,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes a run that is still <see cref="RunStatus.Queued"/> because the engine
    /// refused it before it started.
    /// </summary>
    /// <param name="runId">The run.</param>
    /// <param name="exception">Why it never started.</param>
    /// <param name="correlationId">The id of the log entry that carries the cause.</param>
    /// <returns>A task that completes when the row is closed, or when it was not queued.</returns>
    /// <remarks>
    /// The engine closes every run it started; a run it refused before writing its
    /// row (no checkpoint, a schema that changed, a request no longer pending) would
    /// otherwise stay <see cref="RunStatus.Queued"/> forever. Uses
    /// <see cref="CancellationToken.None"/>: it runs on the failure path.
    /// </remarks>
    public async ValueTask CloseIfQueuedAsync(Guid runId, Exception exception, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            if (await runStore.GetRunAsync(runId, CancellationToken.None).ConfigureAwait(false) is not { Status: RunStatus.Queued } queued)
            {
                return;
            }

            await runStore.CompleteRunAsync(
                new RunCompletion
                {
                    RunId = runId,
                    Status = RunStatus.Failed,
                    CompletedAt = _clock.GetUtcNow(),
                    Error = new RunError
                    {
                        Type = exception.GetType().Name,
                        Message = SafeErrorText.ForPersistence(exception, correlationId),
                    },
                    TenantId = queued.TenantId,
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception storeException)
        {
            // CancellationToken.None: no cancellation of ours (K-840). The job's
            // own outcome still carries the failure.
            logger?.LogWarning(storeException, "Could not close queued workflow run {RunId} as Failed.", runId);
        }
    }

    /// <summary>Reads the final status the engine wrote for a run.</summary>
    /// <param name="runId">The run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The status; <see langword="null"/> when the row is gone.</returns>
    public async ValueTask<RunStatus?> ReadStatusAsync(Guid runId, CancellationToken cancellationToken)
        => (await runStore.GetRunAsync(runId, cancellationToken).ConfigureAwait(false))?.Status;

    private async ValueTask<List<RunRecord>> ReadChainAsync(Guid headRunId, string tenantId, CancellationToken cancellationToken)
    {
        var head = await runStore.GetRunAsync(headRunId, cancellationToken).ConfigureAwait(false);

        if (head is null || !string.Equals(head.TenantId, tenantId, StringComparison.Ordinal))
        {
            return [];
        }

        var chain = new List<RunRecord> { head };
        var seen = new HashSet<Guid> { head.Id };

        while (chain.Count < MaxChainLength)
        {
            // Newest first: if a lease was lost while its owner was still alive
            // (K-886), two continuations of one run can exist, and the newer one
            // is the work in flight.
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

    private async ValueTask<WorkflowCheckpointPointer?> FindCheckpointAsync(
        List<RunRecord> chain,
        string tenantId,
        CancellationToken cancellationToken)
    {
        for (var index = chain.Count - 1; index >= 0; index--)
        {
            var checkpoints = await checkpointStore
                .ListByRunAsync(tenantId, chain[index].Id, cancellationToken)
                .ConfigureAwait(false);

            if (checkpoints.Count > 0)
            {
                return new WorkflowCheckpointPointer(chain[index].Id, checkpoints[^1].CheckpointId);
            }
        }

        return null;
    }
}

/// <summary>What a queued workflow job does with work it finds already started.</summary>
internal enum WorkflowRecoveryStep
{
    /// <summary>The work never started: run the operation under the first run's id.</summary>
    NotStarted,

    /// <summary>The last run completed or waits for input: nothing runs again.</summary>
    Settled,

    /// <summary>The last run was interrupted: a continuation run continues it.</summary>
    Continue,

    /// <summary>The last run was interrupted and the chain is at its limit: the work fails.</summary>
    Exhausted,
}

/// <summary>A run and the checkpoint of it a continuation resumes from.</summary>
/// <param name="RunId">The run that wrote the checkpoint.</param>
/// <param name="CheckpointId">The checkpoint.</param>
internal sealed record WorkflowCheckpointPointer(Guid RunId, string CheckpointId);

/// <summary>The decision <see cref="WorkflowRecovery.PlanAsync"/> made.</summary>
internal sealed record WorkflowRecoveryPlan
{
    /// <summary>Gets the step to take.</summary>
    public required WorkflowRecoveryStep Step { get; init; }

    /// <summary>Gets the chain, first run first; empty when the first run has no row.</summary>
    public required IReadOnlyList<RunRecord> Chain { get; init; }

    /// <summary>
    /// Gets the run the step is about: the settled run, the interrupted run a
    /// continuation continues, or the last run of an exhausted chain.
    /// </summary>
    public RunRecord? Last { get; init; }

    /// <summary>Gets the continuation's id, for <see cref="WorkflowRecoveryStep.Continue"/>.</summary>
    public Guid? ContinuationRunId { get; init; }

    /// <summary>Gets whether the continuation's row already exists (opened earlier and never started).</summary>
    public bool ContinuationIsOpen { get; init; }

    /// <summary>
    /// Gets the latest checkpoint in the chain; <see langword="null"/> when the chain
    /// wrote none and the operation has to start again.
    /// </summary>
    public WorkflowCheckpointPointer? Checkpoint { get; init; }

    /// <summary>Gets the session the chain runs in; the first run's, which every continuation shares.</summary>
    public string? SessionId => Chain.Count > 0 ? Chain[0].SessionId : null;
}
