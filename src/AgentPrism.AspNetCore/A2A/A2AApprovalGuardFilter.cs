using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// A2A uzerinden disa acik agent'larin onay gerektiren bir tool tasimadigini
/// dogrulayan, istek isleme icinde calisan guard.
/// </summary>
/// <remarks>
/// <see cref="McpApprovalGuardFilter"/> ile AYNI gerekce ve AYNI tasarim: bir
/// <c>IHostedService</c> DEGIL, bir arka plan <see cref="Task"/>'tir — gerekce
/// icin <see cref="McpApprovalGuardFilter"/>'in belgesine bakin.
/// </remarks>
internal sealed class A2AApprovalGuardFilter : IEndpointFilter
{
    private readonly Task _checkTask;

    public A2AApprovalGuardFilter(
        SchemaReadyGate schemaReadyGate,
        IAgentCatalog catalog,
        IToolRegistry toolRegistry,
        AgentPrismA2AOptions options,
        IHostApplicationLifetime lifetime,
        ILogger<A2AApprovalGuardFilter> logger)
    {
        ArgumentNullException.ThrowIfNull(schemaReadyGate);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(logger);

        _checkTask = RunCheckAsync(schemaReadyGate, catalog, toolRegistry, options, lifetime, logger);
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        await _checkTask.ConfigureAwait(false);

        return await next(context).ConfigureAwait(false);
    }

    private static async Task RunCheckAsync(
        SchemaReadyGate schemaReadyGate,
        IAgentCatalog catalog,
        IToolRegistry toolRegistry,
        AgentPrismA2AOptions options,
        IHostApplicationLifetime lifetime,
        ILogger logger)
    {
        try
        {
            await schemaReadyGate.WaitAsync(lifetime.ApplicationStopping).ConfigureAwait(false);

            var descriptors = await catalog.ListAsync(lifetime.ApplicationStopping).ConfigureAwait(false);

            ExternalSurfaceGuard.EnsureNoApprovalRequiredTools(
                descriptors, options.ExposedAgents, exposeAll: false, toolRegistry, "A2A");
        }
        catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
        {
            // Uygulama kapaniyor; denetimin sonucu artik onemsiz.
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "A2A disa acik yuzey denetimi basarisiz oldu; uygulama durduruluyor.");

            // Gerekce McpApprovalGuardFilter ile AYNI: StopApplication() Host
            // kendi baslatma dongusunu surdururken cagrilirsa Host.StartAsync
            // OperationCanceledException ile patlar. ApplicationStarted'a kadar
            // ertelenir.
            lifetime.ApplicationStarted.Register(lifetime.StopApplication);

            throw;
        }
    }
}
