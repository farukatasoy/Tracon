using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Handles <see cref="JobHandlerKeys.Workflow"/> jobs. It runs a registered workflow in
/// sequence with each input in the job items.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IWorkflowRunner"/> is <strong>optional</strong>, like the <c>501</c>
/// pattern in <c>WorkflowEndpoints</c>. See
/// <c>Tracon.AspNetCore/Endpoints/WorkflowEndpoints.cs</c>. If the consumer
/// does not add the <c>Tracon.Workflows</c> package or call <c>UseWorkflows()</c>,
/// this handler is registered but no engine exists. The job ends with
/// <see cref="JobStatus.Failed"/> instead of HTTP 501 and <c>ErrorMessage</c> states why.
/// </para>
/// <para>
/// Consuming the stream performs execution. The MAF workflow engine advances the
/// graph as it produces events. The workflow does not run if the stream is not consumed.
/// </para>
/// <para>
/// A structured item (<see cref="BatchItemInput"/>) carries parameter
/// values for the workflow's schema; the runner checks them, so an item with a
/// bad value fails alone and the rest of the batch runs. A workflow run takes no
/// documents: an item that names one fails with that reason instead of running
/// without it. An item's <c>sessionId</c> becomes the run's execution session, so
/// the host can correlate the run (and its <c>run.completed</c> webhook) with its
/// own record.
/// </para>
/// <para>
/// The job's payload may carry the attribution of whoever queued it, in the
/// envelope <see cref="JobPayload"/> describes. Each workflow then runs under
/// <see cref="AmbientRunAttributionScope"/> with that user and those labels, so
/// its root run and every node and tool see them, as on a synchronous run.
/// </para>
/// <para>
/// <strong>Recovery.</strong> While <c>TraconWorkflowOptions.ResumeInterruptedRuns</c>
/// is on, the item's run id is written on the item (a pending report) BEFORE the run
/// starts. A job leased again after its process stopped finds that item still
/// pending with a run id, and <see cref="WorkflowRecovery"/> decides: the run
/// finished (the item is reported), it was interrupted (a continuation resumes it
/// from the latest checkpoint), or the chain is at its limit (the item fails). The
/// job's own attempt limit applies too: its last attempt closes an interrupted item
/// instead of continuing it, so a batch job needs <c>2 + MaxResumeAttempts</c>
/// attempts to get every continuation. While the setting is off, nothing is written
/// before the run and an interrupted item runs again from the beginning, as before.
/// </para>
/// <para>
/// A store error while an item's run id is written or its chain is read or extended
/// fails the attempt, not the item: the item stays pending and the next attempt
/// plans it again. So does a refused continuation whose queued row could not be
/// closed. A run's own failure fails only its item.
/// </para>
/// <para>
/// When the worker stops, the run's stream ends quietly and the run closes as
/// <see cref="RunStatus.Canceled"/>. The item is then NOT reported: the job is
/// leased again later and finds the item pending, which is what recovery needs.
/// </para>
/// </remarks>
internal sealed class WorkflowJobHandler(
    IWorkflowRunner? runner,
    WorkflowRecovery recovery,
    ILogger<WorkflowJobHandler>? logger = null) : IJobHandler
{
    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (runner is null)
        {
            throw new TraconException(
                "The workflow engine is not registered. Add the 'Tracon.Workflows' package and call UseWorkflows().");
        }

        // 🚨 F-364: opened in THIS method's own body, before any item's root
        // starts, and held across every MoveNextAsync of every item's stream.
        // The runner reads the attribution when it builds the root's scope
        // (F-337), so a scope opened later, or in a helper, reaches no node.
        // Without it a workflow queued by a trigger, a schedule or
        // IJobDispatcher ran with no user and no labels.
        var queued = QueuedRunAttribution.Read(context.Job, logger);
        using var attribution = queued is null ? null : AmbientRunAttributionScope.Begin(queued.UserId, queued.Labels);

        foreach (var item in context.Items)
        {
            if (item.Status != JobItemStatus.Pending)
            {
                continue;
            }

            if (await context.IsCancelledAsync(cancellationToken).ConfigureAwait(false))
            {
                break;
            }

            var run = new ItemRun();
            var input = BatchItemInput.Parse(item.Input);

            if (input.Problem is not null || input.Documents.Count > 0)
            {
                run.Failed = true;
                run.Error = input.Problem ?? "A workflow run does not accept documents; the batch item named some.";
            }
            else
            {
                // The recovery step stays OUTSIDE the catch below: a store error
                // while the marker is written or the chain is read or extended
                // fails this attempt, not the item.
                var start = await PrepareItemAsync(context, item, input, queued, run, cancellationToken).ConfigureAwait(false);

                if (start is not null)
                {
                    try
                    {
                        await ConsumeAsync(start(), run, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
                    {
                        var correlationId = SafeErrorText.NewCorrelationId();

                        if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                        {
                            logger.LogWarning(
                                exception,
                                "Batch job item failed: job={JobId} sequence={Seq} workflow={WorkflowName} (ref: {CorrelationId})",
                                context.Job.Id,
                                item.Seq,
                                context.Job.TargetName,
                                correlationId);
                        }

                        // A run the engine refused before it started would stay
                        // Queued: a continuation's row is written before the engine
                        // sees it. If it cannot be closed, the attempt fails and the
                        // item stays pending: the next attempt finds the row.
                        if (run.OpenedRunId is { } opened &&
                            !await recovery.CloseIfQueuedAsync(opened, exception, correlationId).ConfigureAwait(false))
                        {
                            throw new TraconException(
                                $"Workflow run '{opened}' was refused, and its row could not be closed; the job is tried again to close it. (ref: {correlationId})",
                                exception);
                        }

                        run.Failed = true;
                        run.Error = SafeErrorText.ForPersistence(exception, correlationId);
                    }
                }
            }

            // 🚨 The worker is stopping: the stream ended because the token was
            // cancelled, not because the run did. Reporting the item would close
            // it; left pending, the job leased again finds it (and, with recovery
            // on, the run it was processing).
            cancellationToken.ThrowIfCancellationRequested();

            await context.ReportItemAsync(
                new JobItemResult
                {
                    JobId = context.Job.Id,
                    Seq = item.Seq,
                    Status = run.Failed ? JobItemStatus.Failed : JobItemStatus.Completed,
                    RunId = run.RunId,
                    Error = run.Error,
                },
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Settles, closes or prepares one item's workflow, and records what is known on
    /// <paramref name="run"/>.
    /// </summary>
    /// <returns>Starts the item's run when called; <see langword="null"/> when nothing runs.</returns>
    /// <remarks>
    /// Every store call here lets its error propagate; the run itself starts only when
    /// the caller invokes the result, inside the item's own failure handling.
    /// </remarks>
    private async ValueTask<Func<IAsyncEnumerable<RunEvent>>?> PrepareItemAsync(
        JobContext context,
        JobItemRecord item,
        BatchItemInput input,
        JobPayloadContent? queued,
        ItemRun run,
        CancellationToken cancellationToken)
    {
        if (!recovery.IsOn)
        {
            // The behavior before recovery existed: no marker, a fresh run id.
            return () => runner!.RunStreamingAsync(Request(context, input, input.SessionId, runId: null), cancellationToken);
        }

        if (item.RunId is not { } marked)
        {
            // F-321: the run id is on the item BEFORE the run starts, so a job
            // leased again after a crash finds the run this item was processing.
            var runId = TraconId.NewId();

            await context.ReportItemAsync(
                new JobItemResult { JobId = context.Job.Id, Seq = item.Seq, Status = JobItemStatus.Pending, RunId = runId },
                cancellationToken).ConfigureAwait(false);

            run.RunId = runId;

            return () => runner!.RunStreamingAsync(Request(context, input, input.SessionId, runId), cancellationToken);
        }

        var plan = await recovery.PlanAsync(marked, context.Job, cancellationToken).ConfigureAwait(false);
        run.RunId = marked;

        switch (plan.Step)
        {
            case WorkflowRecoveryStep.NotStarted:
                return () => runner!.RunStreamingAsync(Request(context, input, input.SessionId, marked), cancellationToken);

            case WorkflowRecoveryStep.Settled:
                run.RunId = plan.Last!.Id;
                return null;

            case WorkflowRecoveryStep.Exhausted:
                await recovery.CloseChainAsync(plan, cancellationToken).ConfigureAwait(false);
                run.RunId = plan.Last!.Id;
                run.Failed = true;
                run.Error = $"The workflow run was interrupted and is not continued: {plan.NotContinuedReason}.";
                return null;

            default:
                // Opened BEFORE the interrupted run is closed: the closed run
                // names the continuation, and the name must point at a row.
                await recovery.OpenContinuationAsync(plan, queued?.UserId, queued?.Labels, cancellationToken).ConfigureAwait(false);
                await recovery.CloseChainAsync(plan, cancellationToken).ConfigureAwait(false);

                var continuation = plan.ContinuationRunId!.Value;
                run.RunId = continuation;
                run.OpenedRunId = continuation;

                // No checkpoint in the chain: the process stopped before the first
                // super-step ended, so there is nothing to resume from and the item
                // runs again, in the same session.
                return plan.Checkpoint is { } checkpoint
                    ? () => runner!.ResumeStreamingAsync(
                        new WorkflowResumeRequest
                        {
                            RunId = checkpoint.RunId,
                            CheckpointId = checkpoint.CheckpointId,
                            NewRunId = continuation,
                        },
                        cancellationToken)
                    : () => runner!.RunStreamingAsync(Request(context, input, plan.SessionId ?? input.SessionId, continuation), cancellationToken);
        }
    }

    private static WorkflowRunRequest Request(JobContext context, BatchItemInput input, string? sessionId, Guid? runId)
        => new()
        {
            WorkflowName = context.Job.TargetName,
            Message = input.Message,
            Parameters = input.Parameters,
            SessionId = sessionId,
            RunId = runId,
        };

    /// <summary>Drains a run's stream; the engine advances the graph only while it is read.</summary>
    private static async ValueTask ConsumeAsync(IAsyncEnumerable<RunEvent> events, ItemRun run, CancellationToken cancellationToken)
    {
        await foreach (var runEvent in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            run.RunId ??= runEvent.RunId;

            if (runEvent.Type == RunEventType.RunFailed)
            {
                run.Failed = true;
                run.Error = runEvent.Text;
            }
        }
    }

    /// <summary>What one item's run produced; filled as the run advances, so a failure keeps what was known.</summary>
    private sealed class ItemRun
    {
        /// <summary>Gets or sets the run the item reports.</summary>
        public Guid? RunId { get; set; }

        /// <summary>Gets or sets the run whose row this handler wrote before the engine started it.</summary>
        public Guid? OpenedRunId { get; set; }

        /// <summary>Gets or sets whether the item failed.</summary>
        public bool Failed { get; set; }

        /// <summary>Gets or sets the failure message.</summary>
        public string? Error { get; set; }
    }
}
