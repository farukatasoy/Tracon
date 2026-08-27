using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Handles <see cref="JobKind.Workflow"/> jobs. It runs a registered workflow in
/// sequence with each input in the job items.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IWorkflowRunner"/> is <strong>optional</strong>, like the <c>501</c>
/// pattern in <c>WorkflowEndpoints</c>. See
/// <c>AgentPrism.AspNetCore/Endpoints/WorkflowEndpoints.cs</c>. If the consumer
/// does not add the <c>AgentPrism.Workflows</c> package or call <c>UseWorkflows()</c>,
/// this handler is registered but no engine exists. The job ends with
/// <see cref="JobStatus.Failed"/> instead of HTTP 501 and <c>ErrorMessage</c> states why.
/// </para>
/// <para>
/// Consuming the stream performs execution. The MAF workflow engine advances the
/// graph as it produces events. The workflow does not run if the stream is not consumed.
/// </para>
/// </remarks>
internal sealed class WorkflowJobHandler(
    IWorkflowRunner? runner = null,
    ILogger<WorkflowJobHandler>? logger = null) : IJobHandler
{
    /// <inheritdoc />
    public JobKind Kind => JobKind.Workflow;

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (runner is null)
        {
            throw new AgentPrismException(
                "The workflow engine is not registered. Add the 'AgentPrism.Workflows' package and call UseWorkflows().");
        }

        foreach (var item in context.Items)
        {
            if (item.Status != JobItemStatus.Pending)
            {
                continue;
            }

            if (await context.IsCancelledAsync(cancellationToken).ConfigureAwait(false))
            {
                break;
            }

            Guid? runId = null;
            var failed = false;
            string? error = null;

            try
            {
                await foreach (var runEvent in runner
                    .RunStreamingAsync(
                        new WorkflowRunRequest { WorkflowName = context.Job.TargetName, Message = item.Input },
                        cancellationToken)
                    .ConfigureAwait(false))
                {
                    runId ??= runEvent.RunId;

                    if (runEvent.Type == RunEventType.RunFailed)
                    {
                        failed = true;
                        error = runEvent.Text;
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var correlationId = SafeErrorText.NewCorrelationId();

                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(
                        exception,
                        "Batch job item failed: job={JobId} sequence={Seq} workflow={WorkflowName} (ref: {CorrelationId})",
                        context.Job.Id,
                        item.Seq,
                        context.Job.TargetName,
                        correlationId);
                }

                failed = true;
                error = SafeErrorText.ForPersistence(exception, correlationId);
            }

            await context.ReportItemAsync(
                new JobItemResult
                {
                    JobId = context.Job.Id,
                    Seq = item.Seq,
                    Status = failed ? JobItemStatus.Failed : JobItemStatus.Completed,
                    RunId = runId,
                    Error = error,
                },
                cancellationToken).ConfigureAwait(false);
        }
    }
}
