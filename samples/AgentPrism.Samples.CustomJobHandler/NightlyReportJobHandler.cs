using Microsoft.Extensions.Logging;

namespace AgentPrism.Samples.CustomJobHandler;

/// <summary>A handler that generates one nightly report line per job item.</summary>
/// <remarks>
/// Demonstrates the at-least-once contract <see cref="IJobHandler.ExecuteAsync"/>
/// documents: it skips any item not <see cref="JobItemStatus.Pending"/>, so a
/// retry after a crashed worker or an expired lease never re-generates a
/// report that already went out.
/// </remarks>
public sealed class NightlyReportJobHandler(ILogger<NightlyReportJobHandler>? logger = null) : IJobHandler
{
    /// <inheritdoc />
    public JobKind Kind => JobKind.AgentBatch;

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var item in context.Items)
        {
            // context.Items carries EVERY item, including ones a previous,
            // now-dead attempt already completed - skipping here is what
            // makes at-least-once delivery safe for this handler's side effect.
            if (item.Status != JobItemStatus.Pending)
            {
                continue;
            }

            if (await context.IsCancelledAsync(cancellationToken).ConfigureAwait(false))
            {
                break;
            }

            if (logger is not null && logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Nightly report generated for '{Subject}'.", item.Input);
            }

            await context.ReportItemAsync(
                new JobItemResult
                {
                    JobId = context.Job.Id,
                    Seq = item.Seq,
                    Status = JobItemStatus.Completed,
                },
                cancellationToken).ConfigureAwait(false);
        }
    }
}
