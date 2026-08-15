namespace AgentPrism;

/// <summary>Runs a retention sweep (<see cref="JobKind.Retention"/>).</summary>
/// <remarks>
/// <see cref="JobRecord.TargetName"/> is either a specific <see cref="RetentionTargets"/>
/// value or <c>"*"</c>, which processes all active policies. The job has one item.
/// It reports the item when the entire run succeeds or fails.
/// </remarks>
public sealed class RetentionJobHandler(RetentionExecutor executor) : IJobHandler
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
            if (context.Items.Count > 0)
            {
                await context.ReportItemAsync(
                    new JobItemResult
                    {
                        JobId = context.Job.Id,
                        Seq = context.Items[0].Seq,
                        Status = JobItemStatus.Failed,
                        Error = exception.Message,
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
