using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Writes the job behind a queued run whose <see cref="RunStatus.Queued"/>
/// placeholder row is already open, and closes that row when the queue refuses
/// the job.
/// </summary>
/// <remarks>
/// <para>
/// A queued run opens its row BEFORE its job, so <c>GET /api/runs/{id}</c>
/// answers <c>Queued</c> rather than <c>404</c> before the worker picks the job
/// up. If the enqueue then throws, no worker will ever lease the run: the row
/// would stay <c>Queued</c> forever. This closes it as
/// <see cref="RunStatus.Failed"/> and rethrows, so the caller still gets no
/// <c>202</c>.
/// </para>
/// <para>
/// Every path that opens a placeholder row and then enqueues calls this — the
/// queued agent and workflow endpoints and the agent target of an inbound
/// trigger. The approval resume is the exception on purpose: its run id is
/// derived from the approval, so repeating the decision finds the row and
/// writes the missing job. A single-answer workflow request does the same with
/// this method: a repeat of the answer whose job is missing writes it again.
/// </para>
/// <para>
/// <strong>A job that is already there is not a failure.</strong> The job id is
/// the run id, and a store refuses a second job with a stored id
/// (<see cref="IJobStore.EnqueueAsync"/>). When the enqueue throws, the job is
/// read back first: if another caller wrote it (two repeats of the same answer,
/// or a slow first answer and its repeat), that job is returned and the row is
/// left to it. Closing the row there would fail a run its job is running.
/// </para>
/// <para>
/// A run store that also fails here does not hide the queue's exception: the
/// close is logged and the original exception is rethrown. The row then stays
/// <see cref="RunStatus.Queued"/> with no job; run reconciliation reads only
/// running rows and does not close it.
/// </para>
/// <para>
/// <strong>A job the worker ends without calling its handler</strong> closes the row too
/// (<see cref="CloseWhenJobFailsWithoutRunningAsync"/>): the handler is what moves the row on and closes it
/// when it fails, so a job that never reached it left the row <see cref="RunStatus.Queued"/> for ever.
/// </para>
/// </remarks>
internal static class QueuedRunPlaceholder
{
    /// <summary>
    /// The handler keys whose job id is the id of the run the job carries out, and whose run row is opened
    /// <see cref="RunStatus.Queued"/> before the job: the queued agent run, the queued workflow operation, the
    /// resume of an approval and the continuation of an interrupted run.
    /// </summary>
    private static readonly HashSet<string> RunOwningKeys = new(StringComparer.Ordinal)
    {
        JobHandlerKeys.AgentRun,
        JobHandlerKeys.WorkflowRun,
        JobHandlerKeys.ApprovalResume,
        JobHandlerKeys.RunContinuation,
    };

    /// <summary>Enqueues <paramref name="job"/>; closes its run's placeholder row if the enqueue throws.</summary>
    /// <param name="jobStore">The queue.</param>
    /// <param name="runStore">The store that holds the placeholder row.</param>
    /// <param name="job">The job. Its <see cref="JobRecord.Id"/> is the run id.</param>
    /// <param name="items">The job's items.</param>
    /// <param name="logger">Logs the refusal with a correlation id.</param>
    /// <param name="timeProvider">The clock the closed row is stamped with; the system clock when <see langword="null"/> (no clock registered).</param>
    /// <param name="cancellationToken">The cancellation token for the enqueue.</param>
    /// <returns>The stored job.</returns>
    public static async ValueTask<JobRecord> EnqueueAsync(
        IJobStore jobStore,
        IRunStore runStore,
        JobRecord job,
        IReadOnlyList<string> items,
        ILogger logger,
        TimeProvider? timeProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            return await jobStore.EnqueueAsync(job, items, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (await FindWrittenAsync(jobStore, job, logger).ConfigureAwait(false) is { } written)
            {
                return written;
            }

            var correlationId = SafeErrorText.NewCorrelationId();

            logger.LogError(
                exception,
                "Queued run {RunId} could not be queued; its row is closed as Failed. (ref: {CorrelationId})",
                job.Id,
                correlationId);

            try
            {
                await runStore.CompleteRunAsync(
                    new RunCompletion
                    {
                        RunId = job.Id,
                        Status = RunStatus.Failed,
                        CompletedAt = (timeProvider ?? TimeProvider.System).GetUtcNow(),
                        Error = ClosingRunError.From(exception, correlationId),
                        TenantId = job.TenantId,
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception closeException)
            {
                logger.LogWarning(
                    closeException,
                    "Queued run {RunId} stays Queued: its row could not be closed. (ref: {CorrelationId})",
                    job.Id,
                    correlationId);
            }

            throw;
        }
    }

    /// <summary>
    /// Closes the <see cref="RunStatus.Queued"/> row of a run whose job the worker failed without calling its handler.
    /// </summary>
    /// <param name="runStore">The store that holds the row.</param>
    /// <param name="job">The job the worker just failed. Its <see cref="JobRecord.Id"/> is the run id for the keys that carry a run.</param>
    /// <param name="code">The job error code the job failed with.</param>
    /// <param name="correlationId">The id of the log entry that carries the cause.</param>
    /// <param name="logger">Logs a row that could not be closed.</param>
    /// <param name="timeProvider">The clock the closed row is stamped with.</param>
    /// <returns>A task that completes when the row is closed or there was none to close; it does not throw.</returns>
    /// <remarks>
    /// A row that is not <see cref="RunStatus.Queued"/> is left as it is: an earlier attempt that moved it on owns it,
    /// and run reconciliation closes it if that attempt died. A store error is logged and ignored: the job is
    /// already failed and nothing else can be done for the row.
    /// </remarks>
    public static async ValueTask CloseWhenJobFailsWithoutRunningAsync(
        IRunStore runStore,
        JobRecord job,
        string code,
        string correlationId,
        ILogger? logger,
        TimeProvider timeProvider)
    {
        if (!RunOwningKeys.Contains(job.HandlerKey))
        {
            return;
        }

        // The row is read through the ambient tenant. Opened here, in this method's own body, around the awaited
        // calls: the worker reaches this before it opens its own scope for a job whose key or attempts are refused.
        using var tenantScope = AmbientTenantScope.Begin(job.TenantId);

        try
        {
            if (await runStore.GetRunAsync(job.Id, CancellationToken.None).ConfigureAwait(false) is not { Status: RunStatus.Queued })
            {
                return;
            }

            await runStore.CompleteRunAsync(
                new RunCompletion
                {
                    RunId = job.Id,
                    Status = RunStatus.Failed,
                    CompletedAt = timeProvider.GetUtcNow(),
                    Error = ClosingRunError.From(code, JobErrorCodes.Format(code, correlationId)),
                    TenantId = job.TenantId,
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger?.LogWarning(
                exception,
                "Queued run {RunId} stays Queued: its job failed without running and the row could not be closed. (ref: {CorrelationId})",
                job.Id,
                correlationId);
        }
    }

    /// <summary>
    /// Reads back the job an enqueue that threw was writing: a job with its id that is there now
    /// was written by another caller, or by this one before a failure that came after the write.
    /// </summary>
    /// <returns>The stored job; <see langword="null"/> when there is none or it cannot be read.</returns>
    private static async ValueTask<JobRecord?> FindWrittenAsync(IJobStore jobStore, JobRecord job, ILogger logger)
    {
        try
        {
            var written = await jobStore.GetAsync(job.TenantId, job.Id, CancellationToken.None).ConfigureAwait(false);

            if (written is not null && logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Queued run {RunId} already has its job; another call wrote it, so this enqueue is not repeated.",
                    job.Id);
            }

            return written;
        }
        catch (Exception readException)
        {
            logger.LogWarning(readException, "Queued run {RunId}: the job could not be read back after the enqueue failed.", job.Id);

            return null;
        }
    }
}
