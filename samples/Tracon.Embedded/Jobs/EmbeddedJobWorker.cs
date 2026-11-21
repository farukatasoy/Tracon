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
/// </remarks>
internal sealed class EmbeddedJobWorker(
    EmbeddedJobQueue queue,
    IAgentCatalog catalog,
    RunAdmission admission,
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
                    // A job that carries text for the summarizer binds it at resolve time: the catalog checks it
                    // against the agent's schema, compiles the agent for exactly that text, and the run records
                    // it, so a replay of the run sends the same text again.
                    var (agentName, options) = job.ArticleContent is { } article
                        ? ("summarizer", new AgentResolveOptions
                        {
                            Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["articleContent"] = article },
                        })
                        : ("assistant", null);

                    // Running an agent from code passes none of the checks in front of the HTTP endpoints. This job
                    // runs on behalf of a tenant and a user, so it asks them first: the run authorization handler, the
                    // owner of a session it would continue (this job names none) and the tenant's quota.
                    var decision = await admission.CheckAsync(agentName, cancellationToken: stoppingToken).ConfigureAwait(false);

                    if (!decision.IsAllowed)
                    {
                        logger.LogWarning(
                            "Job for tenant {TenantId} refused ({Denial}): {Reason}",
                            job.TenantId,
                            decision.Denial,
                            decision.Reason);
                        continue;
                    }

                    var agent = await catalog.ResolveAsync(agentName, options, stoppingToken).ConfigureAwait(false);

                    if (agent is null)
                    {
                        logger.LogWarning("Job for tenant {TenantId} skipped: the '{AgentName}' agent is not in the catalog.", job.TenantId, agentName);
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
