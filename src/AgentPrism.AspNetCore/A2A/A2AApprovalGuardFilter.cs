using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Guard, running inside request processing, that verifies no agent exposed
/// through A2A carries a tool that requires approval.
/// </summary>
/// <remarks>
/// Same rationale and same design as <see cref="McpApprovalGuardFilter"/>: NOT
/// an <c>IHostedService</c>, but a background <see cref="Task"/> — see
/// <see cref="McpApprovalGuardFilter"/>'s documentation for the rationale.
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

            var descriptors = await ExternalSurfaceGuard
                .ListCatalogWithRetryAsync(catalog, lifetime, logger, "A2A")
                .ConfigureAwait(false);

            ExternalSurfaceGuard.EnsureNoApprovalRequiredTools(
                descriptors, options.ExposedAgents, exposeAll: false, toolRegistry, "A2A");
        }
        catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
        {
            // Application is shutting down; the check's outcome no longer matters.
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "A2A external surface check failed; stopping the application.");

            // Same rationale as McpApprovalGuardFilter: if StopApplication() is
            // called while the Host is still running its own startup loop,
            // Host.StartAsync blows up with an OperationCanceledException. It is
            // deferred until ApplicationStarted.
            lifetime.ApplicationStarted.Register(lifetime.StopApplication);

            throw;
        }
    }
}
