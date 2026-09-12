using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tracon.Embedded;

/// <summary>
/// Runs queued jobs outside any HTTP request — the mandatory scenario this
/// sample exists to prove: <see cref="AmbientTenantScope"/> and
/// <see cref="AmbientRunAttributionScope"/> carry identity through a run that
/// has no request to read a header from.
/// </summary>
/// <remarks>
/// 🚨 Both scopes are opened in THIS method's own body, directly around the
/// call that starts the run — never in a helper this method calls. An
/// <c>AsyncLocal&lt;T&gt;</c> mutation does not flow back out of the method
/// that set it; it only flows DOWN into what that method itself awaits. This
/// exact trap has recurred five times in this repository
/// (docs/hafiza/cekirdek-calistirma.md).
/// </remarks>
internal sealed class EmbeddedJobWorker(
    EmbeddedJobQueue queue,
    IAgentCatalog catalog,
    ILogger<EmbeddedJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            using (AmbientTenantScope.Begin(job.TenantId))
            using (AmbientRunAttributionScope.Begin(job.UserId, labels: null))
            {
                try
                {
                    var agent = await catalog.ResolveAsync("assistant", culture: null, stoppingToken).ConfigureAwait(false);

                    if (agent is null)
                    {
                        logger.LogWarning("Job for tenant {TenantId} skipped: the 'assistant' agent is not in the catalog.", job.TenantId);
                        continue;
                    }

                    await agent.RunAsync(job.Message, cancellationToken: stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Job for tenant {TenantId} failed.", job.TenantId);
                }
            }
        }
    }
}
