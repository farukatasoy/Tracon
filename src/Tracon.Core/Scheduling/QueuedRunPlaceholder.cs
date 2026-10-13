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
/// writes the missing job.
/// </para>
/// <para>
/// A run store that also fails here does not hide the queue's exception: the
/// close is logged and the original exception is rethrown.
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
}
