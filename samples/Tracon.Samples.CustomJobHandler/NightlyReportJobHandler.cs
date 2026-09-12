using Microsoft.Extensions.Logging;

namespace Tracon.Samples.CustomJobHandler;

/// <summary>A handler that generates one nightly report line per job item.</summary>
/// <remarks>
/// <para>
/// Registered under the consumer key
/// <see cref="NightlyReportJobHandlerRegistrationExtensions.HandlerKey"/>. The
/// key -- not the type, and not the registration order -- is what the worker
/// dispatches on, so this handler runs whether it is registered before or
/// after <c>AddTracon()</c>, and it can never be shadowed by a built-in one.
/// </para>
/// <para>
/// Demonstrates the at-least-once contract <see cref="IJobHandler.ExecuteAsync"/>
/// documents: it skips any item not <see cref="JobItemStatus.Pending"/>, so a
/// retry after a crashed worker or an expired lease never re-generates a
/// report that already went out.
/// </para>
/// </remarks>
public sealed class NightlyReportJobHandler(ILogger<NightlyReportJobHandler>? logger = null) : IJobHandler
{
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
