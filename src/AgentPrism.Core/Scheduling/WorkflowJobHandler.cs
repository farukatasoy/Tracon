using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// <see cref="JobKind.Workflow"/> islerini yurutur: kayitli bir workflow'u
/// isin ogelerindeki her girdi ile sirayla calistirir.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IWorkflowRunner"/> <strong>opsiyoneldir</strong> — tipki
/// <c>WorkflowEndpoints</c>'in <c>501</c> deseninde oldugu gibi
/// (bkz. <c>AgentPrism.AspNetCore/Endpoints/WorkflowEndpoints.cs</c>).
/// Tuketici <c>AgentPrism.Workflows</c> paketini eklemediyse veya
/// <c>UseWorkflows()</c> cagirmadiysa bu isleyici kayitlidir ama motor
/// bulunamaz; is HTTP 501 yerine <see cref="JobStatus.Failed"/> ile
/// sonuclanir ve <c>ErrorMessage</c> nedeni acikca soyler.
/// </para>
/// <para>
/// Akisi tuketmek yurutmenin kendisidir: MAF workflow motoru olaylari
/// urettikce grafi ilerletir; akis tuketilmezse workflow calismaz.
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
                "Workflow motoru kayitli degil. 'AgentPrism.Workflows' paketini ekleyip UseWorkflows() cagirin.");
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
                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(
                        exception,
                        "Toplu is ogesi basarisiz oldu: is={JobId} sira={Seq} workflow={WorkflowName}",
                        context.Job.Id,
                        item.Seq,
                        context.Job.TargetName);
                }

                failed = true;
                error = exception.Message;
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
