using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Handles <see cref="JobKind.AgentBatch"/> jobs. It runs a registered agent in
/// sequence for each input in the job items.
/// </summary>
/// <remarks>
/// It follows the same HTTP-independent path as the HTTP layer to resolve and run
/// agents. An agent resolved by <see cref="IAgentCatalog.ResolveAsync(string, string, CancellationToken)"/>
/// is already wrapped in the run-recording decorator, so every run writes a normal
/// <c>runs</c> row through <see cref="IRunStore"/>. It processes items <strong>in sequence</strong>
/// because provider rate limits would constrain parallelism. Parallelism between jobs
/// is already provided by <see cref="AgentPrismSchedulingOptions.MaxConcurrentJobs"/>.
/// </remarks>
internal sealed class AgentBatchJobHandler(
    IAgentCatalog catalog,
    ILogger<AgentBatchJobHandler>? logger = null) : IJobHandler
{
    /// <inheritdoc />
    public JobKind Kind => JobKind.AgentBatch;

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var agent = await catalog.ResolveAsync(context.Job.TargetName, culture: null, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException(
                $"The agent named '{context.Job.TargetName}' was not found. The job will be marked as failed.");

        foreach (var item in context.Items)
        {
            // Retry scenario: when the lease expires and the job is claimed again,
            // items already processed successfully do not run again.
            if (item.Status != JobItemStatus.Pending)
            {
                continue;
            }

            if (await context.IsCancelledAsync(cancellationToken).ConfigureAwait(false))
            {
                break;
            }

            var runId = AgentPrismId.NewId();

            try
            {
                await agent
                    .RunAsync(item.Input, session: null, options: new AgentPrismRunOptions { RunId = runId }, cancellationToken)
                    .ConfigureAwait(false);

                await context.ReportItemAsync(
                    new JobItemResult
                    {
                        JobId = context.Job.Id,
                        Seq = item.Seq,
                        Status = JobItemStatus.Completed,
                        RunId = runId,
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(
                        exception,
                        "Batch job item failed: job={JobId} sequence={Seq} agent={AgentName}",
                        context.Job.Id,
                        item.Seq,
                        context.Job.TargetName);
                }

                await context.ReportItemAsync(
                    new JobItemResult
                    {
                        JobId = context.Job.Id,
                        Seq = item.Seq,
                        Status = JobItemStatus.Failed,
                        RunId = runId,
                        Error = exception.Message,
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
