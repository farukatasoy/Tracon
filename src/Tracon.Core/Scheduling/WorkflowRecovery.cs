using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Finds the run a queued workflow job was running when its process stopped, and
/// decides what the job does with it when it is leased again.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The job is the owner.</strong> Run reconciliation still closes an
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
/// Closed by its job's time limit (error type <c>job_max_duration_exceeded</c>): the work
/// ran out of time and ends; it is never continued.
/// </item>
/// <item>
/// Any other status: the run was interrupted. A continuation still
/// <see cref="RunStatus.Queued"/> counts too: its attempt opened it and stopped
/// before the engine started it. Within <see cref="IWorkflowRecoveryPolicy.MaxResumeAttempts"/>
/// continuations a new run continues it; past it, the work fails.
/// </item>
/// </list>
/// <para>
/// <strong>The job's last attempt.</strong> The worker fails a lease past the job's
/// attempt limit before any handler runs, so a run started on the last attempt has
/// nobody left to close it if it is interrupted too. The last attempt therefore never
/// starts a continuation: it closes the chain, whatever continuations are left. A
/// queued run's job gets <c>2 + MaxResumeAttempts</c> attempts, so its last attempt
/// is the one that finds the chain at its limit; a batch job keeps its own limit and
/// gets at most <c>MaxAttempts - 2</c> continuations per item.
/// </para>
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
/// <strong>Store errors.</strong> Every read and write here lets a store error
/// propagate, and the job handlers let it fail the attempt, not the work: the next
/// attempt plans again from what the store holds. Each write leaves a state the next
/// plan reads correctly: the continuation's row is written before anything names it,
/// and a run left open is closed by whichever attempt finds it.
/// </para>
/// <para>
/// Every store read here takes the job's tenant explicitly; the worker opens the
/// ambient tenant scope too, for the store calls that read it.
/// </para>
/// </remarks>
/// <param name="runStore">The run store.</param>
/// <param name="checkpointStore">The checkpoint store.</param>
/// <param name="policy">The recovery settings; <see langword="null"/> when the workflow engine is not registered.</param>
/// <param name="schedulingOptions">The queue's settings, for the attempt limit of a job that sets none.</param>
/// <param name="clock">The time source.</param>
/// <param name="logger">The logger.</param>
internal sealed class WorkflowRecovery(
    IRunStore runStore,
    IWorkflowCheckpointStore checkpointStore,
    IWorkflowRecoveryPolicy? policy,
    IOptionsMonitor<TraconSchedulingOptions>? schedulingOptions = null,
    TimeProvider? clock = null,
    ILogger? logger = null)
{
    /// <summary>The error type an interrupted run is closed with.</summary>
    public const string InterruptedErrorType = TraconErrorTypes.Interrupted;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>Gets whether recovery is on.</summary>
    public bool IsOn => WorkflowRecoveryPolicy.IsOn(policy);

    /// <summary>Decides what to do with the work that starts with <paramref name="headRunId"/>.</summary>
    /// <param name="headRunId">The first run of the work.</param>
    /// <param name="job">The leased job: its tenant, its attempt and its attempt limit.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The plan.</returns>
    public async ValueTask<WorkflowRecoveryPlan> PlanAsync(
        Guid headRunId,
        JobRecord job,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var chain = await ReadChainAsync(headRunId, job.TenantId, cancellationToken).ConfigureAwait(false);

        if (chain.Count == 0 || (chain.Count == 1 && chain[0].Status == RunStatus.Queued))
        {
            return new WorkflowRecoveryPlan { Step = WorkflowRecoveryStep.NotStarted, Chain = chain };
        }

        var last = chain[^1];

        if (last.Status is RunStatus.Completed or RunStatus.AwaitingInput)
        {
            return new WorkflowRecoveryPlan { Step = WorkflowRecoveryStep.Settled, Chain = chain, Last = last };
        }

        // A run its job stopped at the job's time limit was
        // not interrupted, it ran out of time. Continuing it would make the limit
        // per attempt and the work unbounded; the work ends here instead. Read from
        // the error type the run closed with, the one fact the run row keeps.
        if (string.Equals(last.Error?.Type, JobAttemptSignals.MaxDurationExceededRunErrorType, StringComparison.Ordinal))
        {
            return new WorkflowRecoveryPlan { Step = WorkflowRecoveryStep.Stopped, Chain = chain, Last = last };
        }

        // Interrupted: Running (its process died), Canceled (a graceful stop),
        // Failed (reconciliation closed the orphan), or a continuation still
        // Queued (its attempt opened it and stopped before the engine started it).
        var continuations = chain.Count - 1;

        if (continuations >= (policy?.MaxResumeAttempts ?? 0))
        {
            return new WorkflowRecoveryPlan
            {
                Step = WorkflowRecoveryStep.Exhausted,
                Chain = chain,
                Last = last,
                NotContinuedReason =
                    $"its chain already has {continuations} continuation run(s), and 'Tracon:Workflows:MaxResumeAttempts' allows no more",
            };
        }

        if ((job.MaxAttempts ?? schedulingOptions?.CurrentValue.MaxAttempts) is { } limit && job.Attempt >= limit)
        {
            return new WorkflowRecoveryPlan
            {
                Step = WorkflowRecoveryStep.Exhausted,
                Chain = chain,
                Last = last,
                NotContinuedReason =
                    $"its job is on its last attempt ({job.Attempt} of {limit}), and a continuation started on the last attempt " +
                    "could not be closed if it were interrupted too. Give the job two more attempts than 'Tracon:Workflows:MaxResumeAttempts'",
            };
        }

        return new WorkflowRecoveryPlan
        {
            Step = WorkflowRecoveryStep.Continue,
            Chain = chain,
            Last = last,
            ContinuationRunId = TraconId.NewId(),
            Checkpoint = await FindCheckpointAsync(chain, job.TenantId, cancellationToken).ConfigureAwait(false),
        };
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
    /// The row is <see cref="RunStatus.Queued"/> until then, so an attempt that stops
    /// before the engine starts it leaves a row the next plan counts as interrupted.
    /// Call it BEFORE <see cref="CloseChainAsync"/>, which names this row.
    /// </remarks>
    public async ValueTask OpenContinuationAsync(
        WorkflowRecoveryPlan plan,
        string? userId,
        IReadOnlyDictionary<string, string>? labels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.ContinuationRunId is not { } runId || plan.Last is not { } last)
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
    /// Closes every run of the chain that is still <see cref="RunStatus.Running"/> or
    /// <see cref="RunStatus.Queued"/> as <see cref="RunStatus.Failed"/>, each naming
    /// the run that continues it, or why none does.
    /// </summary>
    /// <param name="plan">A <see cref="WorkflowRecoveryStep.Continue"/> or <see cref="WorkflowRecoveryStep.Exhausted"/> plan.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when every open run of the chain is closed.</returns>
    /// <remarks>
    /// Not only the last run: an earlier attempt may have opened a continuation and
    /// stopped before it closed the run that continuation continues. The process that
    /// was writing a run is gone, so the closing event is written here, one past the
    /// last sequence, as run reconciliation does. A run in another status keeps the
    /// error it was closed with.
    /// </remarks>
    public async ValueTask CloseChainAsync(WorkflowRecoveryPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        for (var index = 0; index < plan.Chain.Count; index++)
        {
            var run = plan.Chain[index];

            if (run.Status is not (RunStatus.Running or RunStatus.Queued))
            {
                continue;
            }

            var successor = index < plan.Chain.Count - 1 ? plan.Chain[index + 1].Id : plan.ContinuationRunId;
            var stopped = run.Status == RunStatus.Queued
                ? "The process running this workflow run stopped before the run started."
                : "The process running this workflow run stopped before the run ended.";
            var message = successor is { } next
                ? $"{stopped} Run '{next}' continues it."
                : $"{stopped} It is not continued: {plan.NotContinuedReason}.";

            await CloseInterruptedAsync(run, message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Closes a run that is still <see cref="RunStatus.Queued"/> because the engine
    /// refused it before it started.
    /// </summary>
    /// <param name="runId">The run.</param>
    /// <param name="exception">Why it never started.</param>
    /// <param name="correlationId">The id of the log entry that carries the cause.</param>
    /// <returns>
    /// <see langword="true"/> when the row is closed or was not queued;
    /// <see langword="false"/> when the store failed and the row may still be queued.
    /// </returns>
    /// <remarks>
    /// The engine closes every run it started; a run it refused before writing its
    /// row (no checkpoint, a schema that changed, a request no longer pending) would
    /// otherwise stay <see cref="RunStatus.Queued"/> forever. Uses
    /// <see cref="CancellationToken.None"/>: it runs on the failure path. On
    /// <see langword="false"/> the caller fails the attempt instead of the work, so
    /// that the next attempt finds the row again and closes it.
    /// </remarks>
    public ValueTask<bool> CloseIfQueuedAsync(Guid runId, Exception exception, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return CloseIfQueuedAsync(
            runId,
            RunStatus.Failed,
            new RunError
            {
                Type = exception.GetType().Name,
                Message = SafeErrorText.ForPersistence(exception, correlationId),
            });
    }

    /// <summary>
    /// Closes a run that is still <see cref="RunStatus.Queued"/> with the given status
    /// and error.
    /// </summary>
    /// <param name="runId">The run.</param>
    /// <param name="status">The status it closes with.</param>
    /// <param name="error">The error it carries.</param>
    /// <returns>
    /// <see langword="true"/> when the row is closed or was not queued;
    /// <see langword="false"/> when the store failed and the row may still be queued.
    /// </returns>
    /// <remarks>
    /// A job that reaches its time limit before the engine opened its run closes the
    /// row this way, with the error a run the engine stopped carries, so the next plan
    /// reads the same fact either way.
    /// </remarks>
    public async ValueTask<bool> CloseIfQueuedAsync(Guid runId, RunStatus status, RunError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            if (await runStore.GetRunAsync(runId, CancellationToken.None).ConfigureAwait(false) is not { Status: RunStatus.Queued } queued)
            {
                return true;
            }

            await runStore.CompleteRunAsync(
                new RunCompletion
                {
                    RunId = runId,
                    Status = status,
                    CompletedAt = _clock.GetUtcNow(),
                    Error = error,
                    TenantId = queued.TenantId,
                },
                CancellationToken.None).ConfigureAwait(false);

            return true;
        }
        catch (Exception storeException)
        {
            // CancellationToken.None: no cancellation of ours.
            logger?.LogWarning(storeException, "Could not close queued workflow run {RunId} as Failed.", runId);

            return false;
        }
    }

    /// <summary>Reads the final status the engine wrote for a run.</summary>
    /// <param name="runId">The run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The status; <see langword="null"/> when the row is gone.</returns>
    public async ValueTask<RunStatus?> ReadStatusAsync(Guid runId, CancellationToken cancellationToken)
        => (await runStore.GetRunAsync(runId, cancellationToken).ConfigureAwait(false))?.Status;

    private async ValueTask CloseInterruptedAsync(RunRecord run, string message, CancellationToken cancellationToken)
    {
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
            // lost write (the old owner still alive and writing) must
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

    private ValueTask<List<RunRecord>> ReadChainAsync(Guid headRunId, string tenantId, CancellationToken cancellationToken)
        => WorkflowContinuationChain.ReadAsync(runStore, headRunId, tenantId, cancellationToken);

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

    /// <summary>
    /// The last run was stopped by its job's time limit: the work ends, and nothing
    /// continues it.
    /// </summary>
    Stopped,

    /// <summary>The last run was interrupted: a continuation run continues it.</summary>
    Continue,

    /// <summary>
    /// The last run was interrupted, and the chain is at its limit or the job is on its
    /// last attempt: the work fails.
    /// </summary>
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

    /// <summary>
    /// Gets why the chain is not continued, for <see cref="WorkflowRecoveryStep.Exhausted"/>:
    /// a clause that follows "It is not continued:".
    /// </summary>
    public string? NotContinuedReason { get; init; }

    /// <summary>
    /// Gets the latest checkpoint in the chain; <see langword="null"/> when the chain
    /// wrote none and the operation has to start again.
    /// </summary>
    public WorkflowCheckpointPointer? Checkpoint { get; init; }

    /// <summary>Gets the session the chain runs in; the first run's, which every continuation shares.</summary>
    public string? SessionId => Chain.Count > 0 ? Chain[0].SessionId : null;

    /// <summary>
    /// Gets the definition version the chain's first run was compiled from; <see langword="null"/>
    /// when that run recorded none (a workflow defined in code, or a row from before versions were
    /// recorded). A run that starts again, because the chain wrote no checkpoint, names it, so an
    /// edit made while the job waited does not change what runs.
    /// </summary>
    public int? DefinitionVersion => Chain.Count > 0 ? Chain[0].AgentVersion : null;
}
