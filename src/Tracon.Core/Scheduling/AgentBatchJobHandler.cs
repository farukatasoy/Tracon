using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Handles <see cref="JobHandlerKeys.AgentBatch"/> jobs. It runs a registered agent in
/// sequence for each input in the job items.
/// </summary>
/// <remarks>
/// It follows the same HTTP-independent path as the HTTP layer to resolve and run
/// agents. An agent resolved by <see cref="IAgentCatalog.ResolveAsync(string, string, CancellationToken)"/>
/// is already wrapped in the run-recording decorator, so every run writes a normal
/// <c>runs</c> row through <see cref="IRunStore"/>. It processes items <strong>in sequence</strong>
/// because provider rate limits would constrain parallelism. Parallelism between jobs
/// is already provided by <see cref="TraconSchedulingOptions.MaxConcurrentJobs"/>.
/// </remarks>
internal sealed class AgentBatchJobHandler(
    IAgentCatalog catalog,
    ILogger<AgentBatchJobHandler>? logger = null) : IJobHandler
{
    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Resolved once up front so a job naming no agent fails as a whole, as before.
        var probe = await catalog.ResolveAsync(context.Job.TargetName, culture: null, cancellationToken).ConfigureAwait(false)
            ?? throw new TraconException(
                $"The agent named '{context.Job.TargetName}' was not found. The job will be marked as failed.");

        // F-330: a batch carries no parameter values. An agent whose required
        // parameter has no default fails the job once, here, rather than once
        // per item with the same error.
        if (probe.GetService<UnboundParameterGuard>() is { RequiresValues: true } unbound)
        {
            throw unbound.Failure();
        }

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

            var runId = TraconId.NewId();

            try
            {
                // F-310: resolved again for every item. A batch can run for hours, and an
                // agent resolved once kept its MCP tools bound to a connection that a
                // server edit or delete had closed - every later item called it. A
                // cache hit while nothing changed. An item whose agent no longer
                // resolves fails on its own, like an item whose run fails.
                var agent = await catalog.ResolveAsync(context.Job.TargetName, culture: null, cancellationToken).ConfigureAwait(false)
                    ?? throw new TraconException($"The agent named '{context.Job.TargetName}' no longer exists.");

                await agent
                    .RunAsync(item.Input, session: null, options: new TraconRunOptions { RunId = runId }, cancellationToken)
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
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
            {
                var correlationId = SafeErrorText.NewCorrelationId();

                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(
                        exception,
                        "Batch job item failed: job={JobId} sequence={Seq} agent={AgentName} (ref: {CorrelationId})",
                        context.Job.Id,
                        item.Seq,
                        context.Job.TargetName,
                        correlationId);
                }

                await context.ReportItemAsync(
                    new JobItemResult
                    {
                        JobId = context.Job.Id,
                        Seq = item.Seq,
                        Status = JobItemStatus.Failed,
                        RunId = runId,
                        Error = SafeErrorText.ForPersistence(exception, correlationId),
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
