using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>Runs a retention sweep (<see cref="JobKind.Retention"/>).</summary>
/// <remarks>
/// <see cref="JobRecord.TargetName"/> is either a specific <see cref="RetentionTargets"/>
/// value or <c>"*"</c>, which processes all active policies. The job has one item.
/// It reports the item when the entire run succeeds or fails.
/// </remarks>
internal sealed class RetentionJobHandler(
    RetentionExecutor executor,
    ILogger<RetentionJobHandler>? logger = null) : IJobHandler
{
    /// <inheritdoc />
    public JobKind Kind => JobKind.Retention;

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var target = context.Job.TargetName is "*" or "" ? null : context.Job.TargetName;

        try
        {
            await executor.RunAsync(context.Job.TenantId, target, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var correlationId = SafeErrorText.NewCorrelationId();

            if (logger is not null && logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(exception, "Retention job {JobId} failed. (ref: {CorrelationId})", context.Job.Id, correlationId);
            }

            if (context.Items.Count > 0)
            {
                await context.ReportItemAsync(
                    new JobItemResult
                    {
                        JobId = context.Job.Id,
                        Seq = context.Items[0].Seq,
                        Status = JobItemStatus.Failed,
                        Error = SafeErrorText.ForPersistence(exception, correlationId),
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            throw;
        }

        if (context.Items.Count > 0)
        {
            await context.ReportItemAsync(
                new JobItemResult
                {
                    JobId = context.Job.Id,
                    Seq = context.Items[0].Seq,
                    Status = JobItemStatus.Completed,
                },
                cancellationToken).ConfigureAwait(false);
        }
    }
}
