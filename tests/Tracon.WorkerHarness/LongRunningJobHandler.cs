using Microsoft.Extensions.Options;
using Tracon.Tests.Common;

namespace Tracon.WorkerHarness;

/// <summary>
/// A job handler that takes a configured amount of time, so a test can kill
/// the process while it is halfway through.
/// </summary>
/// <remarks>
/// It skips items that are not <see cref="JobItemStatus.Pending"/>, the same
/// contract every shipped handler follows (see <see cref="IJobHandler"/>) -
/// the point of the failure proof is what Tracon does around a correct
/// handler, not what a careless one does.
/// </remarks>
internal sealed class LongRunningJobHandler(IOptions<WorkerHarnessSettingsHolder> settings) : IJobHandler
{
    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var harness = settings.Value.Settings;

        HarnessExecutionLog.Append(
            harness.ExecutionLogPath,
            HarnessExecutionLog.StartedStage,
            harness.Name,
            context.Job.Id,
            context.Job.Attempt);

        foreach (var item in context.Items)
        {
            if (item.Status != JobItemStatus.Pending)
            {
                continue;
            }

            await Task.Delay(harness.WorkDuration, cancellationToken).ConfigureAwait(false);

            await context.ReportItemAsync(
                new JobItemResult
                {
                    JobId = context.Job.Id,
                    Seq = item.Seq,
                    Status = JobItemStatus.Completed,
                },
                cancellationToken).ConfigureAwait(false);
        }

        HarnessExecutionLog.Append(
            harness.ExecutionLogPath,
            HarnessExecutionLog.FinishedStage,
            harness.Name,
            context.Job.Id,
            context.Job.Attempt);
    }
}

/// <summary>Carries <see cref="WorkerHarnessSettings"/> into the handler's scope.</summary>
/// <remarks>
/// The handler is resolved from a fresh scope per execution, so it takes its
/// configuration through the options pipeline rather than a static.
/// </remarks>
internal sealed class WorkerHarnessSettingsHolder
{
    public WorkerHarnessSettings Settings { get; set; } = null!;
}
