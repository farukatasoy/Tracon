using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Handles <see cref="JobHandlerKeys.Workflow"/> jobs. It runs a registered workflow in
/// sequence with each input in the job items.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IWorkflowRunner"/> is <strong>optional</strong>, like the <c>501</c>
/// pattern in <c>WorkflowEndpoints</c>. See
/// <c>Tracon.AspNetCore/Endpoints/WorkflowEndpoints.cs</c>. If the consumer
/// does not add the <c>Tracon.Workflows</c> package or call <c>UseWorkflows()</c>,
/// this handler is registered but no engine exists. The job ends with
/// <see cref="JobStatus.Failed"/> instead of HTTP 501 and <c>ErrorMessage</c> states why.
/// </para>
/// <para>
/// Consuming the stream performs execution. The MAF workflow engine advances the
/// graph as it produces events. The workflow does not run if the stream is not consumed.
/// </para>
/// <para>
/// F-294: a structured item (<see cref="BatchItemInput"/>) carries parameter
/// values for the workflow's schema; the runner checks them, so an item with a
/// bad value fails alone and the rest of the batch runs. A workflow run takes no
/// documents: an item that names one fails with that reason instead of running
/// without it.
/// </para>
/// </remarks>
internal sealed class WorkflowJobHandler(
    IWorkflowRunner? runner = null,
    ILogger<WorkflowJobHandler>? logger = null) : IJobHandler
{
    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (runner is null)
        {
            throw new TraconException(
                "The workflow engine is not registered. Add the 'Tracon.Workflows' package and call UseWorkflows().");
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

            var input = BatchItemInput.Parse(item.Input);

            if (input.Problem is not null || input.Documents.Count > 0)
            {
                failed = true;
                error = input.Problem ?? "A workflow run does not accept documents; the batch item named some.";
            }

            try
            {
                await foreach (var runEvent in RunItemAsync(runner, context.Job.TargetName, input, failed, cancellationToken)
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
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
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

    /// <summary>Runs one item's workflow, or nothing when the item already failed its own checks.</summary>
    private static async IAsyncEnumerable<RunEvent> RunItemAsync(
        IWorkflowRunner runner,
        string workflowName,
        BatchItemInput input,
        bool alreadyFailed,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (alreadyFailed)
        {
            yield break;
        }

        await foreach (var runEvent in runner
            .RunStreamingAsync(
                new WorkflowRunRequest { WorkflowName = workflowName, Message = input.Message, Parameters = input.Parameters },
                cancellationToken)
            .ConfigureAwait(false))
        {
            yield return runEvent;
        }
    }
}
