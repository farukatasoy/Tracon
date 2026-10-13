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
/// is on, the job is given <c>1 + MaxResumeAttempts</c> attempts. Leased again after
/// its process stopped, it finds the queued run no longer <see cref="RunStatus.Queued"/>,
/// and <see cref="WorkflowRecovery"/> decides whether the work finished, is continued
/// from the latest checkpoint, or fails at the limit. With no checkpoint in the
/// chain, the operation is applied again: the run starts again in the same session,
/// or the resume or the response is repeated.
/// </para>
/// <para>
/// When the worker stops, the run closes as <see cref="RunStatus.Canceled"/> and the
/// job is left to its lease: it is leased again, or it fails on its attempt limit.
/// </para>
/// </remarks>
internal sealed class WorkflowRunJobHandler(
    IWorkflowRunner? runner,
    WorkflowRecovery recovery,
    ILogger<WorkflowRunJobHandler>? logger = null) : IJobHandler
{
    /// <summary>The job's payload cannot be run.</summary>
    internal const string PayloadInvalidCode = "tracon.job.workflow-run-payload-invalid";

    /// <summary>The run was refused, failed or was cancelled; the run's own error says why.</summary>
    internal const string RunFailedCode = "tracon.job.workflow-run-failed";

    /// <summary>The run was interrupted and its chain is at the continuation limit.</summary>
    internal const string NotContinuedCode = "tracon.job.workflow-run-not-continued";

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

        var current = request.RunId;

        try
        {
            IAsyncEnumerable<RunEvent> events;

            if (!recovery.IsOn)
            {
                events = Apply(context.Job.TargetName, request, request.RunId, sessionId: null, cancellationToken);
            }
            else
            {
                var plan = await recovery.PlanAsync(request.RunId, context.Job.TenantId, cancellationToken).ConfigureAwait(false);

                switch (plan.Step)
                {
                    case WorkflowRecoveryStep.NotStarted:
                        events = Apply(context.Job.TargetName, request, request.RunId, sessionId: null, cancellationToken);
                        break;

                    case WorkflowRecoveryStep.Settled:
                        return;

                    case WorkflowRecoveryStep.Exhausted:
                        await recovery.CloseInterruptedAsync(plan.Last!, null, plan.Chain.Count - 1, cancellationToken).ConfigureAwait(false);

                        throw await RefuseAsync(
                            runId: null,
                            NotContinuedCode,
                            new TraconException(
                                $"Workflow run '{plan.Last!.Id}' was interrupted and is not continued: its chain already has " +
                                $"{plan.Chain.Count - 1} continuation run(s), and 'Tracon:Workflows:MaxResumeAttempts' allows no more.")).ConfigureAwait(false);

                    default:
                        var continuation = plan.ContinuationRunId!.Value;

                        await recovery.CloseInterruptedAsync(plan.Last!, continuation, plan.Chain.Count - 1, cancellationToken).ConfigureAwait(false);
                        await recovery.OpenContinuationAsync(plan, request.UserId, request.Labels, cancellationToken).ConfigureAwait(false);

                        current = continuation;

                        // No checkpoint in the chain: the process stopped before
                        // the first super-step ended. The operation is applied again
                        // under the continuation's id, in the chain's session.
                        events = plan.Checkpoint is { } checkpoint
                            ? runner.ResumeStreamingAsync(
                                new WorkflowResumeRequest
                                {
                                    RunId = checkpoint.RunId,
                                    CheckpointId = checkpoint.CheckpointId,
                                    NewRunId = continuation,
                                },
                                cancellationToken)
                            : Apply(context.Job.TargetName, request, continuation, plan.SessionId, cancellationToken);
                        break;
                }
            }

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

        // 🚨 The worker is stopping: the stream ended because the token was
        // cancelled, not because the run did. The job is left to its lease.
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
                runId: null,
                RunFailedCode,
                new TraconException($"Workflow run '{current}' ended with status {status}.")).ConfigureAwait(false);
        }
    }

    /// <summary>Starts the queued operation under <paramref name="runId"/>.</summary>
    /// <param name="workflowName">The workflow a run starts (the job's target).</param>
    /// <param name="request">The queued operation.</param>
    /// <param name="runId">The id the opened run carries.</param>
    /// <param name="sessionId">The chain's session for a run that starts again; <see langword="null"/> for the payload's.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The run's event stream.</returns>
    private IAsyncEnumerable<RunEvent> Apply(
        string workflowName,
        WorkflowRunJobRequest request,
        Guid runId,
        string? sessionId,
        CancellationToken cancellationToken)
        => request.Operation switch
        {
            WorkflowRunJobPayload.ResumeOperation => runner!.ResumeStreamingAsync(
                new WorkflowResumeRequest
                {
                    RunId = request.SourceRunId!.Value,
                    CheckpointId = request.CheckpointId,
                    NewRunId = runId,
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
                },
                cancellationToken),
        };

    /// <summary>
    /// Logs the cause once, closes a run the engine never opened, and builds the
    /// exception that fails the job without a retry.
    /// </summary>
    /// <param name="runId">A run whose row may still be queued; <see langword="null"/> to close none.</param>
    /// <param name="code">The stable code the job's error starts with.</param>
    /// <param name="exception">The cause.</param>
    /// <returns>The exception to throw.</returns>
    private async ValueTask<NonRetryableJobException> RefuseAsync(Guid? runId, string code, Exception exception)
    {
        var correlationId = SafeErrorText.NewCorrelationId();

        if (logger is not null && logger.IsEnabled(LogLevel.Error))
        {
            logger.LogError(exception, "Queued workflow run {RunId} failed: {Code}. (ref: {CorrelationId})", runId, code, correlationId);
        }

        if (runId is { } queued)
        {
            await recovery.CloseIfQueuedAsync(queued, exception, correlationId).ConfigureAwait(false);
        }

        return new NonRetryableJobException(code, correlationId, exception);
    }
}
