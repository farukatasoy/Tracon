using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// <see cref="JobKind.AgentBatch"/> islerini yurutur: kayitli bir agent'i
/// isin ogelerindeki her girdi uzerinde sirayla calistirir.
/// </summary>
/// <remarks>
/// Agent'i cozmek ve calistirmak icin HTTP katmaninin kullandigi ayni
/// HTTP-bagimsiz yol izlenir: <see cref="IAgentCatalog.ResolveAsync"/> ile
/// cozulen agent zaten calistirma kaydi dekoratoru ile sarilidir; her
/// calistirma normal bir <c>runs</c> satiri olarak <see cref="IRunStore"/>'a
/// yazilir. Ogeler <strong>sirayla</strong> islenir (Faz 17 acik sorusu 1,
/// oneri kabul edildi): paralellik saglayici hiz sinirina takilirdi; isler
/// arasi paralellik <see cref="AgentPrismSchedulingOptions.MaxConcurrentJobs"/>
/// ile zaten saglanir.
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

        var agent = await catalog.ResolveAsync(context.Job.TargetName, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException(
                $"'{context.Job.TargetName}' adinda bir agent bulunamadi. Is basarisiz olarak isaretlenecek.");

        foreach (var item in context.Items)
        {
            // Yeniden deneme senaryosu: kira suresi dolup is yeniden alindiginda
            // daha once basariyla islenmis ogeler tekrar calistirilmaz.
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
                        "Toplu is ogesi basarisiz oldu: is={JobId} sira={Seq} agent={AgentName}",
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
