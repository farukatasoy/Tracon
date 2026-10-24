using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Executes <see cref="JobHandlerKeys.WorkflowRun"/> jobs: one workflow run, resume
/// or response, queued with <c>Prefer: respond-async</c>.
/// </summary>
/// <remarks>
/// <para>
/// The run is not bound to an HTTP connection, so nobody reading the stream can
/// stop it by going away (only a streamed run ends with its reader). The HTTP layer
/// already checked the caller and wrote the run's <see cref="RunStatus.Queued"/>
/// row under the id it returned with the <c>202</c>; this handler opens the run
/// under that same id (<see cref="JobRecord.Id"/> carries it too), and the engine's
/// write upserts the row. The payload is read again as untrusted input
/// (<see cref="WorkflowRunJobPayload"/>), and the engine checks parameters and the
/// answer's shape again: it is the authority.
/// </para>
/// <para>
/// <strong>The job's outcome follows the run's.</strong> A run that ends
/// <see cref="RunStatus.Completed"/> or <see cref="RunStatus.AwaitingInput"/>
/// completes the job. A run that fails or is cancelled on its own fails the job
/// at once, with no retry: the job's attempts are kept for interruptions, and a
/// failed run leased again would look like one.
/// </para>
/// <para>
/// <strong>Recovery.</strong> While <c>TraconWorkflowOptions.ResumeInterruptedRuns</c>
/// is on, the job is given <c>2 + MaxResumeAttempts</c> attempts: one for the run,
/// one per continuation, and a last one that finds the chain at its limit and closes
/// it. The worker fails a lease past the limit before any handler runs, so without
/// that last attempt the last continuation would stay <see cref="RunStatus.Running"/>.
/// Leased again after its process stopped, the job finds the queued run no longer
/// <see cref="RunStatus.Queued"/>, and <see cref="WorkflowRecovery"/> decides whether
/// the work finished, is continued from the latest checkpoint, or fails at the limit
/// (<see cref="NotContinuedCode"/>). With no checkpoint in the chain, the operation
/// is applied again: the run starts again in the same session, or the resume or the
/// response is repeated.
/// </para>
/// <para>
/// <strong>Store errors.</strong> A store error while the job reads or extends the
/// chain fails the attempt, not the work: the worker retries it while attempts are
/// left, and the next attempt plans again. So does a refused run whose queued row
/// could not be closed: the next attempt finds the row and closes it.
/// </para>
/// <para>
/// When the worker stops, the run closes as <see cref="RunStatus.Canceled"/> and the
/// job is left to its lease: it is leased again, or it fails on its attempt limit. When
/// the job is cancelled, the worker cancels the token within one poll interval and the
/// run closes <see cref="RunStatus.Canceled"/>; the job stays cancelled.
/// </para>
/// <para>
/// <strong>Time limit.</strong> When the attempt reaches the job's time limit
/// (<see cref="JobRecord.MaxDuration"/>), the run closes <see cref="RunStatus.Canceled"/>
/// with the error type <c>job_max_duration_exceeded</c>, and the job fails at once with
/// <see cref="JobErrorCodes.MaxDurationExceeded"/>. It is not an interruption: recovery
/// never continues such a run, so the limit ends the work.
/// </para>
/// </remarks>
internal sealed class WorkflowRunJobHandler(
    IWorkflowRunner? runner,
    WorkflowRecovery recovery,
    ILogger<WorkflowRunJobHandler>? logger = null) : IJobHandler
{
    /// <summary>The job's payload cannot be run.</summary>
    internal const string PayloadInvalidCode = TraconErrorTypes.JobWorkflowRunPayloadInvalid;

    /// <summary>The run was refused, failed or was cancelled; the run's own error says why.</summary>
    internal const string RunFailedCode = TraconErrorTypes.JobWorkflowRunFailed;

    /// <summary>The run was interrupted and its chain is at the continuation limit.</summary>
    internal const string NotContinuedCode = TraconErrorTypes.JobWorkflowRunNotContinued;

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (request, payloadRunId, problem) = WorkflowRunJobPayload.Read(context.Job.Payload, logger);

        if (request is null)
        {
            throw await RefuseAsync(payloadRunId, PayloadInvalidCode, new TraconException(problem!)).ConfigureAwait(false);
        }

        if (runner is null)
        {
            throw await RefuseAsync(
                request.RunId,
                RunFailedCode,
                new TraconException(
                    "The workflow engine is not registered. Add the 'Tracon.Workflows' package and call UseWorkflows().")).ConfigureAwait(false);
        }

        // 🚨 Opened in THIS method's own body and held across every
        // MoveNextAsync of the run's stream: the engine reads the attribution
        // when it builds the root's scope (F-337), and an AsyncLocal written in
        // a helper would not flow back here (F-364).
        using var attribution = AmbientRunAttributionScope.Begin(request.UserId, request.Labels);

        // A job cancelled after it was leased: the cancel endpoint already
        // closed the queued row, and starting the run would reopen it.
        if (await context.IsCancelledAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // The recovery step stays OUTSIDE the catch below: a store error while
        // the chain is read or extended fails this attempt, not the work. The
        // writes are ordered so that the next attempt's plan reads whatever they
        // left: the continuation's row is written before the closed run names it.
        var plan = recovery.IsOn
            ? await recovery.PlanAsync(request.RunId, context.Job, cancellationToken).ConfigureAwait(false)
            : null;
        var current = request.RunId;

        if (plan is { Step: WorkflowRecoveryStep.Settled })
        {
            return;
        }

        // An earlier attempt reached the job's time limit and stopped the run, but its
        // outcome was never written (a store error, a crash right after). The work ran
        // out of time: it ends now, as it would have then.
        if (plan is { Step: WorkflowRecoveryStep.Stopped })
        {
            throw await RefuseAsync(
                plan.Last!.Id,
                JobErrorCodes.MaxDurationExceeded,
                new TraconException($"Workflow run '{plan.Last!.Id}' was stopped by its job's time limit and is not continued."),
                closeIfQueued: false).ConfigureAwait(false);
        }

        if (plan is { Step: WorkflowRecoveryStep.Exhausted })
        {
            await recovery.CloseChainAsync(plan, cancellationToken).ConfigureAwait(false);

            throw await RefuseAsync(
                plan.Last!.Id,
                NotContinuedCode,
                new TraconException(
                    $"Workflow run '{plan.Last!.Id}' was interrupted and is not continued: {plan.NotContinuedReason}."),
                closeIfQueued: false).ConfigureAwait(false);
        }

        if (plan is { Step: WorkflowRecoveryStep.Continue })
        {
            await recovery.OpenContinuationAsync(plan, request.UserId, request.Labels, cancellationToken).ConfigureAwait(false);
            await recovery.CloseChainAsync(plan, cancellationToken).ConfigureAwait(false);

            current = plan.ContinuationRunId!.Value;
        }

        try
        {
            var events = plan is { Step: WorkflowRecoveryStep.Continue }
                ? Continue(context.Job.TargetName, request, plan, cancellationToken)
                : Apply(context.Job.TargetName, request, request.RunId, sessionId: null, definitionVersion: null, cancellationToken);

            await foreach (var runEvent in events.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                _ = runEvent;
            }
        }
        catch (Exception exception) when (exception is not NonRetryableJobException &&
                                          OperationCancellation.IsFailure(exception, cancellationToken))
        {
            // The engine refused the operation before it opened the run (no
            // checkpoint, a schema that changed, a request no longer pending):
            // the queued row it never wrote would otherwise stay Queued.
            throw await RefuseAsync(current, RunFailedCode, exception).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (StoppedAtTimeLimit(context))
        {
            // The limit arrived before the engine opened the run; its row is closed below.
        }

        // 🚨 F-323, open question 13-A: the job's time limit ends the WORK, not the
        // attempt. Left to the lease like a shutdown, the next attempt would continue
        // the run from its checkpoint, and the limit would stop nothing.
        if (StoppedAtTimeLimit(context))
        {
            await StopAtTimeLimitAsync(context, current).ConfigureAwait(false);

            return;
        }

        // 🚨 The worker is stopping, or the job was cancelled: the stream ended
        // because the token was cancelled, not because the run did. A stopping
        // worker leaves the job to its lease; a cancelled job stays cancelled.
        cancellationToken.ThrowIfCancellationRequested();

        RunStatus? status;

        try
        {
            status = await recovery.ReadStatusAsync(current, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            // 🚨 F-348's rule: a store error AFTER the work must not fail the
            // finished work or make the worker run it again. The engine already
            // closed the run, and its row carries the outcome; the job completes.
            logger?.LogWarning(
                exception,
                "Could not read the final status of queued workflow run {RunId}; the job is completed and the run's row carries its outcome.",
                current);

            return;
        }

        if (status is RunStatus.Failed or RunStatus.Canceled)
        {
            // Not an interruption: the run itself ended this way, and its row
            // says why. Failing the job without a retry keeps a second lease
            // from mistaking it for one.
            throw await RefuseAsync(
                current,
                RunFailedCode,
                new TraconException($"Workflow run '{current}' ended with status {status}."),
                closeIfQueued: false).ConfigureAwait(false);
        }
    }

    /// <summary>Gets whether the worker cancelled the handler's token at the job's time limit.</summary>
    /// <param name="context">The job context.</param>
    /// <returns><see langword="true"/> when the time limit, and not a shutdown or a cancel, stopped the attempt.</returns>
    private static bool StoppedAtTimeLimit(JobContext context)
        => context.Signals?.Reason == JobStopReason.MaxDurationExceeded;

    /// <summary>
    /// Ends the work of an attempt the job's time limit stopped: the run is closed with
    /// the limit as its reason, and the job fails without another attempt.
    /// </summary>
    /// <param name="context">The job context.</param>
    /// <param name="runId">The run the attempt was running.</param>
    /// <returns>A task that completes only when the run finished in the same instant; otherwise it throws.</returns>
    /// <exception cref="NonRetryableJobException">The work ended at the limit.</exception>
    /// <exception cref="TraconException">The run's queued row could not be closed; the job is tried again to close it.</exception>
    /// <remarks>
    /// <para>
    /// The engine closes a run it was running with the limit as its reason (the error
    /// type <c>job_max_duration_exceeded</c>), so a later plan never continues it. A run
    /// the engine had not opened yet is still <see cref="RunStatus.Queued"/>; it is
    /// closed here with the same reason.
    /// </para>
    /// <para>
    /// A run that completed in the same instant keeps its outcome: the job completes, as
    /// it does for any handler that returns after its limit.
    /// </para>
    /// </remarks>
    private async ValueTask StopAtTimeLimitAsync(JobContext context, Guid runId)
    {
        var signals = context.Signals!;

        try
        {
            if (await recovery.ReadStatusAsync(runId, signals.WorkerStopping).ConfigureAwait(false) is RunStatus.Completed or RunStatus.AwaitingInput)
            {
                return;
            }
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, signals.WorkerStopping))
        {
            // The status only tells a finished run apart; the run is closed below either way.
            logger?.LogWarning(exception, "Could not read queued workflow run {RunId} after its job's time limit.", runId);
        }

        var error = signals.ToRunError("Workflow run");
        var correlationId = SafeErrorText.NewCorrelationId();

        if (logger is not null && logger.IsEnabled(LogLevel.Error))
        {
            logger.LogError(
                "Queued workflow run {RunId} was stopped by its job's time limit of {MaxDuration}; the job fails and the run is not continued. (ref: {CorrelationId})",
                runId,
                signals.MaxDuration,
                correlationId);
        }

        if (!await recovery.CloseIfQueuedAsync(runId, RunStatus.Canceled, error).ConfigureAwait(false))
        {
            throw new TraconException(
                $"Queued workflow run '{runId}' was stopped by its job's time limit, and its row could not be closed; the job is tried again to close it. (ref: {correlationId})");
        }

        throw new NonRetryableJobException(
            JobErrorCodes.MaxDurationExceeded,
            correlationId,
            new TraconException(error.Message));
    }

    /// <summary>Starts the continuation a <see cref="WorkflowRecoveryStep.Continue"/> plan opened.</summary>
    /// <param name="workflowName">The workflow a run starts (the job's target).</param>
    /// <param name="request">The queued operation.</param>
    /// <param name="plan">The plan.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The continuation's event stream.</returns>
    /// <remarks>
    /// With no checkpoint in the chain the process stopped before the first super-step
    /// ended: the operation is applied again under the continuation's id, in the
    /// chain's session, and a run compiles the definition version the chain's first run
    /// recorded. A resume or a response carries its own version in the payload.
    /// </remarks>
    private IAsyncEnumerable<RunEvent> Continue(
        string workflowName,
        WorkflowRunJobRequest request,
        WorkflowRecoveryPlan plan,
        CancellationToken cancellationToken)
        => plan.Checkpoint is { } checkpoint
            ? runner!.ResumeStreamingAsync(
                new WorkflowResumeRequest
                {
                    RunId = checkpoint.RunId,
                    CheckpointId = checkpoint.CheckpointId,
                    NewRunId = plan.ContinuationRunId!.Value,
                },
                cancellationToken)
            : Apply(workflowName, request, plan.ContinuationRunId!.Value, plan.SessionId, plan.DefinitionVersion, cancellationToken);

    /// <summary>Starts the queued operation under <paramref name="runId"/>.</summary>
    /// <param name="workflowName">The workflow a run starts (the job's target).</param>
    /// <param name="request">The queued operation.</param>
    /// <param name="runId">The id the opened run carries.</param>
    /// <param name="sessionId">The chain's session for a run that starts again; <see langword="null"/> for the payload's.</param>
    /// <param name="definitionVersion">The version the chain's first run recorded, for a run that starts again; <see langword="null"/> runs the current definition.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The run's event stream.</returns>
    private IAsyncEnumerable<RunEvent> Apply(
        string workflowName,
        WorkflowRunJobRequest request,
        Guid runId,
        string? sessionId,
        int? definitionVersion,
        CancellationToken cancellationToken)
        => request.Operation switch
        {
            WorkflowRunJobPayload.ResumeOperation => runner!.ResumeStreamingAsync(
                new WorkflowResumeRequest
                {
                    RunId = request.SourceRunId!.Value,
                    CheckpointId = request.CheckpointId,
                    NewRunId = runId,
                    DefinitionVersion = request.DefinitionVersion,
                },
                cancellationToken),

            WorkflowRunJobPayload.RespondOperation => runner!.RespondStreamingAsync(
                new WorkflowRespondRequest
                {
                    RunId = request.SourceRunId!.Value,
                    RequestId = request.RequestId!,
                    Approved = request.Approved,
                    Text = request.Text,
                    Json = request.Json,
                    CheckpointId = request.CheckpointId,
                    NewRunId = runId,
                    DefinitionVersion = request.DefinitionVersion,
                },
                cancellationToken),

            _ => runner!.RunStreamingAsync(
                new WorkflowRunRequest
                {
                    WorkflowName = workflowName,
                    Message = request.Message,
                    SessionId = sessionId ?? request.SessionId,
                    RunId = runId,
                    Parameters = request.Parameters,
                    DefinitionVersion = definitionVersion,
                },
                cancellationToken),
        };

    /// <summary>
    /// Logs the cause once, closes a run the engine never opened, and builds the
    /// exception that fails the job without a retry.
    /// </summary>
    /// <param name="runId">The run the log entry names; <see langword="null"/> when the payload names none.</param>
    /// <param name="code">The stable code the job's error starts with.</param>
    /// <param name="exception">The cause.</param>
    /// <param name="closeIfQueued">Whether the run's row may still be queued and is closed; <see langword="false"/> when its outcome is already written.</param>
    /// <returns>
    /// The exception to throw: a <see cref="NonRetryableJobException"/>, or, when the
    /// queued row could not be closed, an exception the worker retries, so the next
    /// attempt finds the row and closes it.
    /// </returns>
    private async ValueTask<Exception> RefuseAsync(Guid? runId, string code, Exception exception, bool closeIfQueued = true)
    {
        var correlationId = SafeErrorText.NewCorrelationId();

        if (logger is not null && logger.IsEnabled(LogLevel.Error))
        {
            logger.LogError(exception, "Queued workflow run {RunId} failed: {Code}. (ref: {CorrelationId})", runId, code, correlationId);
        }

        if (closeIfQueued && runId is { } queued && !await recovery.CloseIfQueuedAsync(queued, exception, correlationId).ConfigureAwait(false))
        {
            return new TraconException(
                $"Queued workflow run '{queued}' was refused, and its row could not be closed; the job is tried again to close it. (ref: {correlationId})",
                exception);
        }

        return new NonRetryableJobException(code, correlationId, exception);
    }
}
