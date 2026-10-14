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
/// </remarks>
internal static class QueuedRunPlaceholder
{
    /// <summary>Enqueues <paramref name="job"/>; closes its run's placeholder row if the enqueue throws.</summary>
    /// <param name="jobStore">The queue.</param>
    /// <param name="runStore">The store that holds the placeholder row.</param>
    /// <param name="job">The job. Its <see cref="JobRecord.Id"/> is the run id.</param>
    /// <param name="items">The job's items.</param>
    /// <param name="logger">Logs the refusal with a correlation id.</param>
    /// <param name="cancellationToken">The cancellation token for the enqueue.</param>
    /// <returns>The stored job.</returns>
    public static async ValueTask<JobRecord> EnqueueAsync(
        IJobStore jobStore,
        IRunStore runStore,
        JobRecord job,
        IReadOnlyList<string> items,
        ILogger logger,
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
                        CompletedAt = DateTimeOffset.UtcNow,
                        Error = new RunError
                        {
                            Type = exception.GetType().Name,
                            Message = SafeErrorText.ForPersistence(exception, correlationId),
                        },
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
    /// Reads back the job an enqueue that threw was writing: a job with its id that is there now
    /// was written by another caller, or by this one before a failure that came after the write.
    /// </summary>
    /// <returns>The stored job; <see langword="null"/> when there is none or it cannot be read.</returns>
    private static async ValueTask<JobRecord?> FindWrittenAsync(IJobStore jobStore, JobRecord job, ILogger logger)
    {
        try
        {
            var written = await jobStore.GetAsync(job.TenantId, job.Id, CancellationToken.None).ConfigureAwait(false);

            if (written is not null)
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
