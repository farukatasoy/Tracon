namespace AgentPrism;

/// <summary>Bir saklama suprusunu yurutur (<see cref="JobKind.Retention"/>).</summary>
/// <remarks>
/// <see cref="JobRecord.TargetName"/> ya belirli bir <see cref="RetentionTargets"/>
/// degeri ya da tum etkin politikalari isleyen <c>"*"</c>'tir. Isin tek bir
/// ogesi vardir; oge, kosunun tamami basarili/basarisiz oldugunda raporlanir.
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
