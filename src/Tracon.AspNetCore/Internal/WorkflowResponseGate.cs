using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Decides what an answer to a workflow's pending human request does: start a run, follow the
/// run an identical answer already started, or conflict with a different answer.
/// </summary>
/// <remarks>
/// <para>
/// Runs after every other check of <c>POST /api/workflows/runs/{runId}/respond</c> and before
/// the stream opens or the job is queued, so each outcome is a status code.
/// </para>
/// <para>
/// <strong>The audit entry comes first.</strong> Every answer that starts a run, in either mode,
/// is written as <c>workflow.respond</c> with <see cref="AuditRecorder.WriteOrThrowAsync"/>
/// BEFORE its run row and its record: an entry that cannot be written refuses the answer. An
/// answer that starts nothing (it follows, or it conflicts) changes nothing and writes nothing -
/// the approval inbox does not write a repeated decision again either. The entry carries a hash
/// of the answer, never the answer: the audit trail is not protected at rest.
/// </para>
/// <para>
/// <strong>The run row comes before the record.</strong> The run an answer starts is opened -
/// <see cref="RunStatus.Running"/> for a streamed answer, <see cref="RunStatus.Queued"/> for a
/// queued one - before its record is written, so a record never names a run that does not
/// exist. A repeat answer reads the record and follows that run; a record without a run would
/// lock the request for good. Every write from the run row on uses
/// <see cref="CancellationToken.None"/>: a caller who goes away between the row and the record
/// must not leave one without the other.
/// </para>
/// <para>
/// <strong>The record is the authority.</strong> In
/// <see cref="WorkflowResponseMode.SingleAnswer"/> the claim in
/// <see cref="IWorkflowResponseStore"/> decides which of two concurrent answers starts the run.
/// An answer that loses the claim closes the run it opened as <see cref="RunStatus.Canceled"/>
/// with the error type <see cref="AlreadyAnsweredErrorType"/>; its audit entry names that run.
/// The close is a plain row update: no webhook, metric or run event sink fires for it, because
/// those fire from the engine's run writer, which never started. In
/// <see cref="WorkflowResponseMode.Branch"/> the record is the link from the source run to its
/// answers. A record that cannot be written refuses the answer in both modes, and the run opened
/// for it is closed as <see cref="RunStatus.Failed"/>.
/// </para>
/// </remarks>
internal static class WorkflowResponseGate
{
    /// <summary>The audit action of an answer that starts a run.</summary>
    public const string AuditAction = "workflow.respond";

    /// <summary>The error type of a run that lost the claim to another answer before it started.</summary>
    public const string AlreadyAnsweredErrorType = "request_already_answered";

    /// <summary>The error type of a run whose answer could not be recorded, so it never started.</summary>
    public const string NotRecordedErrorType = "answer_not_recorded";

    /// <summary>
    /// How many times a claim that keeps changing hands under this answer is read again before
    /// the answer gives up. Each turn means another answer won a race; three in a row is not a
    /// state a request reaches in practice.
    /// </summary>
    private const int DecisionAttempts = 3;

    /// <summary>What an answer does.</summary>
    public enum Outcome
    {
        /// <summary>Start the run <see cref="Decision.RunId"/>; its row is already open.</summary>
        Start,

        /// <summary>
        /// Follow the run an identical answer started; when that queued run has no job
        /// (<see cref="Decision.MissingJob"/>), write the job first.
        /// </summary>
        Follow,

        /// <summary>A different answer holds the request.</summary>
        Conflict,

        /// <summary>
        /// An identical answer holds the request, but the run it started no longer exists (a data
        /// subject erasure removed it); there is nothing to follow.
        /// </summary>
        Gone,
    }

    /// <summary>Reads the mode the source run is answered under.</summary>
    /// <param name="source">The run that waits.</param>
    /// <param name="runner">The workflow engine, which knows a workflow defined in code.</param>
    /// <param name="definitions">The definition store.</param>
    /// <param name="tenantId">The caller's tenant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The mode of the definition version the source recorded; otherwise the current one's.</returns>
    /// <remarks>
    /// The source run's recorded version decides, the same version the answer's run compiles by
    /// default: an edit made while the run waits changes neither the graph nor the rule. A run
    /// that recorded no version, a version no longer kept, and a workflow defined in code read
    /// the mode the catalog describes now.
    /// </remarks>
    public static async ValueTask<WorkflowResponseMode> ResolveModeAsync(
        RunRecord source,
        IWorkflowRunner runner,
        IWorkflowDefinitionStore definitions,
        string tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(definitions);

        var name = source.WorkflowName!;

        if (source.AgentVersion is { } recorded)
        {
            var current = await definitions.GetAsync(tenantId, name, cancellationToken).ConfigureAwait(false);

            if (current is not null && current.Version == recorded)
            {
                return current.ResponseMode;
            }

            if (current is not null &&
                await definitions.GetVersionAsync(tenantId, name, recorded, cancellationToken).ConfigureAwait(false) is { } version)
            {
                return version.ResponseMode;
            }
        }

        return (await runner.GetAsync(name, cancellationToken).ConfigureAwait(false))?.ResponseMode
            ?? WorkflowResponseMode.Branch;
    }

    /// <summary>Decides, audits, opens the run and records one answer.</summary>
    /// <param name="answer">The answer and the run it answers.</param>
    /// <param name="services">The request's services.</param>
    /// <param name="cancellationToken">The request's token; used only until the run row is opened.</param>
    /// <returns>What the answer does.</returns>
    /// <exception cref="TraconException">The audit entry could not be written; nothing was opened or recorded.</exception>
    public static async ValueTask<Decision> DecideAsync(
        Answer answer,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(services);

        var store = services.GetRequiredService<IWorkflowResponseStore>();
        var actor = services.GetRequiredService<IAuditActorResolver>().Resolve();

        if (answer.Mode == WorkflowResponseMode.Branch)
        {
            // K-130 kept: every answer opens its own run. The record claims nothing.
            await AuditAsync(answer, answer.NewRunId, redrive: false, actor, services, cancellationToken).ConfigureAwait(false);
            await OpenRunAsync(answer, answer.NewRunId, services).ConfigureAwait(false);
            await RecordOrCloseAsync(
                    answer,
                    answer.NewRunId,
                    services,
                    () => store.AddAsync(Record(answer, answer.NewRunId, claimScope: null, actor), CancellationToken.None).AsTask())
                .ConfigureAwait(false);

            await MarkAnsweredAsync(answer, answer.NewRunId, services).ConfigureAwait(false);

            return Start(answer.NewRunId, redrive: false);
        }

        var scope = answer.Source.SessionId!;

        for (var attempt = 1; attempt <= DecisionAttempts; attempt++)
        {
            var existing = await store.FindClaimAsync(scope, answer.RequestId, cancellationToken).ConfigureAwait(false);

            if (existing is null)
            {
                await AuditAsync(answer, answer.NewRunId, redrive: false, actor, services, cancellationToken).ConfigureAwait(false);
                await OpenRunAsync(answer, answer.NewRunId, services).ConfigureAwait(false);

                var claim = await RecordOrCloseAsync(
                        answer,
                        answer.NewRunId,
                        services,
                        () => store.AddAsync(Record(answer, answer.NewRunId, scope, actor), CancellationToken.None).AsTask())
                    .ConfigureAwait(false);

                if (claim.Claimed)
                {
                    await MarkAnsweredAsync(answer, answer.NewRunId, services).ConfigureAwait(false);

                    return Start(answer.NewRunId, redrive: false);
                }

                // Lost the race. The run opened for this answer never starts; its audit entry
                // stays and names it, as the answer that was tried.
                await CloseLostRunAsync(answer, answer.NewRunId, services).ConfigureAwait(false);

                existing = claim.Existing;

                if (existing is null)
                {
                    // The claim changed hands between the write and the read; the next turn reads
                    // it again, with a fresh run id.
                    answer = answer with { NewRunId = TraconId.NewId() };
                    continue;
                }
            }

            if (!string.Equals(existing.AnswerHash, answer.AnswerHash, StringComparison.Ordinal))
            {
                return new Decision(Outcome.Conflict, Guid.Empty, Guid.Empty, Redrive: false, MissingJob: false);
            }

            var state = await ReadStateAsync(existing, answer.TenantId, services, cancellationToken).ConfigureAwait(false);

            if (state.Gone)
            {
                return new Decision(Outcome.Gone, existing.ResponseRunId, Guid.Empty, Redrive: false, MissingJob: false);
            }

            if (!state.Stopped)
            {
                return new Decision(Outcome.Follow, existing.ResponseRunId, state.InFlight, Redrive: false, state.MissingJob);
            }

            // The run this answer started stopped before it finished with it: the same answer
            // starts the work again (the approval inbox's "the same decision re-drives the
            // hand-off"). The replacement is a compare-and-swap; of two re-drives one wins and
            // the other closes its own run and follows the winner on the next turn.
            var redriveRunId = TraconId.NewId();

            await AuditAsync(answer, redriveRunId, redrive: true, actor, services, cancellationToken).ConfigureAwait(false);
            await OpenRunAsync(answer, redriveRunId, services).ConfigureAwait(false);

            var replaced = await RecordOrCloseAsync(
                    answer,
                    redriveRunId,
                    services,
                    () => store.ReplaceAsync(existing.ResponseRunId, Record(answer, redriveRunId, scope, actor), CancellationToken.None).AsTask())
                .ConfigureAwait(false);

            if (replaced)
            {
                await MarkAnsweredAsync(answer, redriveRunId, services).ConfigureAwait(false);

                return Start(redriveRunId, redrive: true);
            }

            await CloseLostRunAsync(answer, redriveRunId, services).ConfigureAwait(false);
        }

        throw new TraconException(
            $"The answer to request '{answer.RequestId}' of run '{answer.Source.Id}' kept losing to other answers " +
            "given at the same time. Read the run's answers with GET /api/workflows/runs/{runId}/responses and try again.");
    }

    /// <summary>
    /// Reads whether the run an answer started stopped before it finished with the answer, so the
    /// same answer may start the work again, and which run a follower watches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The run's recovery chain (<see cref="WorkflowContinuationChain"/>) is read forward from the
    /// run the answer started; its LAST run decides, because a queued answer that was interrupted
    /// continues under a new run. Only a chain that ended <see cref="RunStatus.Failed"/> or
    /// <see cref="RunStatus.Canceled"/> stopped; a running, queued, waiting or completed one did not.
    /// </para>
    /// <para>
    /// <strong>The queue job is the owner while it lives.</strong> A queued answer's job carries
    /// the run's id. While that job is not finished, its recovery continues the chain after a
    /// restart or a reconciled orphan; the same answer starting a run beside it would apply the
    /// answer twice. The answer then follows instead.
    /// </para>
    /// <para>
    /// <strong>A queued run with no job gets its job.</strong> A chain of one
    /// <see cref="RunStatus.Queued"/> run whose job does not exist was left between its record and
    /// its enqueue (the process stopped, or the queue refused the job and the row could not be
    /// closed either). Nothing else moves such a run: run reconciliation reads only running rows,
    /// and a queued run with no job cannot be cancelled. The follower is told to write the job
    /// (<see cref="Decision.MissingJob"/>); the queue's refusal of a second job with the same id
    /// keeps two followers from starting the work twice.
    /// </para>
    /// <para>
    /// <strong>Delivery.</strong> A record still <see cref="WorkflowResponseStatus.Accepted"/> with a
    /// chain of one run was never handed to the workflow, so starting again is safe. Any other
    /// stopped chain - a <see cref="WorkflowResponseStatus.Delivered"/> record, or an accepted one
    /// that a recovery continuation carried (the continuation applies the answer under its own id,
    /// and only the first run's delivery is recorded) - starts again only when no run of the chain
    /// wrote a checkpoint: there is nothing to resume from, and leaving it would lock the request
    /// for good.
    /// </para>
    /// <para>
    /// A record whose run no longer exists is <see cref="Outcome.Gone"/>: the row was opened before
    /// the record (see the type's remarks), so a missing row was removed later, by a data subject
    /// erasure.
    /// </para>
    /// </remarks>
    private static async ValueTask<RecordState> ReadStateAsync(
        WorkflowResponseRecord existing,
        string tenantId,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var runs = services.GetRequiredService<IRunStore>();
        var chain = await WorkflowContinuationChain
            .ReadAsync(runs, existing.ResponseRunId, tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (chain.Count == 0)
        {
            return new RecordState(Gone: true, Stopped: false, Guid.Empty, MissingJob: false);
        }

        var last = chain[^1];
        var jobs = services.GetRequiredService<IJobStore>();

        if (last.Status is not (RunStatus.Failed or RunStatus.Canceled))
        {
            // A queued answer's row opens Queued before its record and its job (the job id is the
            // run id). A process that stopped between the record and the enqueue, or an enqueue
            // whose row could not be closed either, left a row no worker will ever lease: the
            // follower writes the job.
            var missingJob = chain.Count == 1
                && last.Status == RunStatus.Queued
                && await jobs.GetAsync(tenantId, existing.ResponseRunId, cancellationToken).ConfigureAwait(false) is null;

            return new RecordState(Gone: false, Stopped: false, last.Id, missingJob);
        }

        if (await jobs.GetAsync(tenantId, existing.ResponseRunId, cancellationToken).ConfigureAwait(false)
            is { Status: JobStatus.Pending or JobStatus.Leased or JobStatus.Running })
        {
            return new RecordState(Gone: false, Stopped: false, last.Id, MissingJob: false);
        }

        if (existing.Status == WorkflowResponseStatus.Accepted && chain.Count == 1)
        {
            return new RecordState(Gone: false, Stopped: true, last.Id, MissingJob: false);
        }

        var checkpoints = services.GetRequiredService<IWorkflowCheckpointStore>();

        foreach (var run in chain)
        {
            if ((await checkpoints.ListByRunAsync(tenantId, run.Id, cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                return new RecordState(Gone: false, Stopped: false, last.Id, MissingJob: false);
            }
        }

        return new RecordState(Gone: false, Stopped: true, last.Id, MissingJob: false);
    }

    /// <summary>
    /// Opens the row of the run an answer starts, before the record that names it is written.
    /// </summary>
    private static async ValueTask OpenRunAsync(Answer answer, Guid runId, IServiceProvider services)
        => await services.GetRequiredService<IRunStore>().StartRunAsync(
            new RunStartInfo
            {
                RunId = runId,
                AgentName = answer.Source.WorkflowName!,
                Kind = RunKind.Workflow,
                WorkflowName = answer.Source.WorkflowName,

                // A queued run stays Queued until the worker starts it (a run cancelled from the
                // queue closes like any queued run); a streamed one starts on this request.
                Status = answer.Queued ? RunStatus.Queued : RunStatus.Running,
                StartedAt = DateTimeOffset.UtcNow,
                TenantId = answer.TenantId,
                UserId = answer.UserId,
                Labels = answer.Labels,
                SessionId = answer.Source.SessionId,
                IsStreaming = true,
            },
            CancellationToken.None).ConfigureAwait(false);

    /// <summary>
    /// Writes the record of an answer whose run is already open; when the write fails, closes that
    /// run as <see cref="RunStatus.Failed"/> and lets the failure through.
    /// </summary>
    private static async ValueTask<T> RecordOrCloseAsync<T>(
        Answer answer,
        Guid runId,
        IServiceProvider services,
        Func<Task<T>> write)
    {
        try
        {
            return await write().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await CloseAsync(
                answer,
                runId,
                RunStatus.Failed,
                new RunError
                {
                    Type = NotRecordedErrorType,
                    Message = $"The answer to request '{answer.RequestId}' could not be recorded, so this run never started.",
                },
                services).ConfigureAwait(false);

            throw new TraconException(
                $"The answer to request '{answer.RequestId}' of run '{answer.Source.Id}' was not applied because it could not be recorded.",
                exception);
        }
    }

    /// <summary>
    /// Marks the request answered in the inbox, so the tenant-wide list shows it as answered and
    /// the expiry closes its run as completed instead of failed. Best-effort: the answer is already
    /// recorded and its run is open, and a failed mark is logged.
    /// </summary>
    /// <remarks>
    /// An answer that cannot be marked leaves its request listed as pending until it expires; the
    /// run it started is unaffected. The mark names the run of the LATEST answer, so a re-driven
    /// answer (<see cref="WorkflowResponseStatus.Abandoned"/> replaced) names the run to watch.
    /// </remarks>
    private static async ValueTask MarkAnsweredAsync(Answer answer, Guid responseRunId, IServiceProvider services)
    {
        var requests = services.GetService<IWorkflowRequestStore>();

        if (requests is null)
        {
            return;
        }

        try
        {
            await requests.MarkAnsweredAsync(
                answer.Source.SessionId!,
                answer.RequestId,
                responseRunId,
                DateTimeOffset.UtcNow,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, CancellationToken.None))
        {
            services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Tracon.WorkflowEndpoints")
                .LogWarning(
                    exception,
                    "The answer to request {RequestId} (run {RunId}) could not be marked in the workflow request inbox; the request stays listed as pending until it expires.",
                    answer.RequestId,
                    responseRunId);
        }
    }

    /// <summary>Closes the run of an answer that lost the request to another answer.</summary>
    private static ValueTask CloseLostRunAsync(Answer answer, Guid runId, IServiceProvider services)
        => CloseAsync(
            answer,
            runId,
            RunStatus.Canceled,
            new RunError
            {
                Type = AlreadyAnsweredErrorType,
                Message = $"Request '{answer.RequestId}' was already answered by another call, so this run never started.",
            },
            services);

    /// <summary>
    /// Closes a run the engine never started. Best-effort: the decision is already made, and a
    /// failed close is logged.
    /// </summary>
    /// <remarks>
    /// A row that cannot be closed stays as it was opened. Run reconciliation, when it is on,
    /// closes a <see cref="RunStatus.Running"/> one; nothing closes a <see cref="RunStatus.Queued"/>
    /// one, because reconciliation reads only running rows. Either way no request waits on it: the
    /// row belongs to an answer that lost the claim (the record names the winner's run) or whose
    /// record was never written.
    /// </remarks>
    private static async ValueTask CloseAsync(
        Answer answer,
        Guid runId,
        RunStatus status,
        RunError error,
        IServiceProvider services)
    {
        try
        {
            await services.GetRequiredService<IRunStore>().CompleteRunAsync(
                new RunCompletion
                {
                    RunId = runId,
                    Status = status,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Error = error,
                    TenantId = answer.TenantId,
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, CancellationToken.None))
        {
            services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Tracon.WorkflowEndpoints")
                .LogWarning(
                    exception,
                    "Run {RunId}, opened for an answer to request {RequestId} that never started, could not be closed.",
                    runId,
                    answer.RequestId);
        }
    }

    private static Decision Start(Guid runId, bool redrive) => new(Outcome.Start, runId, runId, redrive, MissingJob: false);

    private static WorkflowResponseRecord Record(Answer answer, Guid runId, string? claimScope, string? actor)
        => new()
        {
            ResponseRunId = runId,
            SourceRunId = answer.Source.Id,
            SessionId = answer.Source.SessionId!,
            RequestId = answer.RequestId,
            AnswerHash = answer.AnswerHash,
            ClaimScope = claimScope,
            Status = WorkflowResponseStatus.Accepted,
            RespondedBy = actor,
            RespondedAt = DateTimeOffset.UtcNow,
            TenantId = answer.TenantId,
        };

    private static ValueTask AuditAsync(
        Answer answer,
        Guid responseRunId,
        bool redrive,
        string? actor,
        IServiceProvider services,
        CancellationToken cancellationToken)
        => AuditRecorder.WriteOrThrowAsync(
            services.GetRequiredService<IAuditLog>(),
            actor,
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Tracon.WorkflowEndpoints"),
            services.GetService<TraconMetrics>(),
            answer.TenantId,
            action: AuditAction,
            entity: $"workflow:{answer.Source.WorkflowName}",
            before: null,
            after: AuditPayload.Write(writer =>
            {
                writer.WriteString("sourceRunId", answer.Source.Id);
                writer.WriteString("requestId", answer.RequestId);
                writer.WriteString("responseRunId", responseRunId);
                writer.WriteString("answerHash", answer.AnswerHash);
                writer.WriteString("mode", answer.Mode.ToString());
                writer.WriteBoolean("redrive", redrive);
            }),
            refusal: $"The answer to request '{answer.RequestId}' of run '{answer.Source.Id}' was not applied",
            timeProvider: null,
            cancellationToken);

    /// <summary>One answer, as the gate needs it.</summary>
    /// <param name="Source">The run that waits; a workflow run with a session.</param>
    /// <param name="RequestId">The request answered.</param>
    /// <param name="AnswerHash">The answer's digest (<see cref="WorkflowAnswerDigest"/>).</param>
    /// <param name="Mode">The mode the source is answered under.</param>
    /// <param name="NewRunId">The run id a fresh answer starts.</param>
    /// <param name="TenantId">The caller's tenant.</param>
    /// <param name="Queued">Whether the answer is queued (<c>Prefer: respond-async</c>) or streamed.</param>
    /// <param name="UserId">The caller's user, read once through <c>RunAttributionReader</c>.</param>
    /// <param name="Labels">The caller's labels, read in the same read.</param>
    public sealed record Answer(
        RunRecord Source,
        string RequestId,
        string AnswerHash,
        WorkflowResponseMode Mode,
        Guid NewRunId,
        string TenantId,
        bool Queued,
        string? UserId,
        IReadOnlyDictionary<string, string>? Labels);

    /// <summary>What the gate decided.</summary>
    /// <param name="Outcome">What the answer does.</param>
    /// <param name="RunId">
    /// The run to start (its row is open), or the run the identical answer started (what its
    /// <c>202</c> named); empty for a conflict.
    /// </param>
    /// <param name="FollowRunId">
    /// The run a streamed follower watches first: the last run of <paramref name="RunId"/>'s
    /// recovery chain, which is the work in flight when a queued answer was interrupted and
    /// continued.
    /// </param>
    /// <param name="Redrive">Whether the run starts the work of a stopped answer again.</param>
    /// <param name="MissingJob">
    /// Whether the followed run is a queued run whose job was never written: the follower writes
    /// it before it follows (see <see cref="ReadStateAsync"/>).
    /// </param>
    public readonly record struct Decision(Outcome Outcome, Guid RunId, Guid FollowRunId, bool Redrive, bool MissingJob);

    /// <summary>What a stored answer's run chain says about it.</summary>
    /// <param name="Gone">The record's run no longer exists.</param>
    /// <param name="Stopped">The work stopped before it finished with the answer.</param>
    /// <param name="InFlight">The run a follower watches first.</param>
    /// <param name="MissingJob">The record's run is a queued run with no job.</param>
    private readonly record struct RecordState(bool Gone, bool Stopped, Guid InFlight, bool MissingJob);
}
