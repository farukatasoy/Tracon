using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Guard, running inside request processing, that verifies no agent exposed
/// through MCP carries a tool that requires approval.
/// </summary>
/// <remarks>
/// <para>
/// This check used to run synchronously inside <c>MapAgentPrismMcpServer</c>,
/// at endpoint-mapping time (BEFORE migrations start), and the catalog query
/// crashed with "no such table" against an empty database. The check was
/// MOVED HERE.
/// </para>
/// <para>
/// The check is a SINGLE background <see cref="Task"/> that STARTS when this
/// filter is instantiated (at Map* time) and lives for the application's
/// lifetime — it waits for <see cref="SchemaReadyGate"/>, then reads the
/// catalog and validates it. Every request awaits the SAME Task: if the check
/// has not finished yet, the request waits for it; if it has finished
/// (successfully or not), the request gets the result immediately. This way
/// no request can get ahead of the check.
/// </para>
/// <para>
/// DELIBERATELY NOT an <c>IHostedService</c>: if an
/// <c>IHostedService.StartAsync</c> synchronously awaited
/// <see cref="SchemaReadyGate"/> and the consumer called <c>UseMcpServer()</c>
/// BEFORE <c>UseSqlite()</c>/<c>UsePostgreSql()</c>/<c>UseSqlServer()</c>, the
/// general Host's sequential <c>IHostedService</c> startup loop would
/// SILENTLY DEADLOCK FOREVER (migration never runs, the guard can never wait
/// for it). that principle that "<c>IServiceCollection</c> registration is
/// order-independent" applies here too; a background Task carries none of
/// this restriction.
/// </para>
/// <para>
/// If the check fails, <see cref="IHostApplicationLifetime.StopApplication"/>
/// is called — the application stops itself even if no traffic ever arrives,
/// so today's "the application never comes up with a wrong configuration"
/// contract is preserved in spirit (only the timing changes: not at startup,
/// but when the check completes).
/// </para>
/// </remarks>
internal sealed class McpApprovalGuardFilter : IEndpointFilter
{
    private readonly Task _checkTask;

    public McpApprovalGuardFilter(
        SchemaReadyGate schemaReadyGate,
        IAgentCatalog catalog,
        IToolRegistry toolRegistry,
        IOptionsMonitor<AgentPrismMcpServerOptions> optionsMonitor,
        IHostApplicationLifetime lifetime,
        ILogger<McpApprovalGuardFilter> logger)
    {
        ArgumentNullException.ThrowIfNull(schemaReadyGate);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(logger);

        _checkTask = RunCheckAsync(schemaReadyGate, catalog, toolRegistry, optionsMonitor, lifetime, logger);
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
        IOptionsMonitor<AgentPrismMcpServerOptions> optionsMonitor,
        IHostApplicationLifetime lifetime,
        ILogger logger)
    {
        try
        {
            await schemaReadyGate.WaitAsync(lifetime.ApplicationStopping).ConfigureAwait(false);

            var options = optionsMonitor.CurrentValue;
            var descriptors = await ExternalSurfaceGuard
                .ListCatalogWithRetryAsync(catalog, lifetime, logger, "MCP")
                .ConfigureAwait(false);

            ExternalSurfaceGuard.EnsureNoApprovalRequiredTools(
                descriptors, options.ExposedAgents, options.ExposeAllAgents, toolRegistry, "MCP");
        }
        catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
        {
            // Application is shutting down; the check's outcome no longer
            // matters (same pattern as McpDiscoveryService).
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "MCP external surface check failed; stopping the application.");

            // 🚨 StopApplication() is NOT called DIRECTLY here: this check can
            // complete while the Host is still running its own IHostedService
            // startup loop (if SchemaReadyGate opens immediately, e.g. no SQL
            // provider is registered at all). Calling StopApplication() at
            // that point triggers Host.StartAsync's OWN cancellation check and
            // BLOWS IT UP with an OperationCanceledException — a confusing
            // error appears instead of the InvalidOperationException that
            // should surface before the first request. ApplicationStarted is
            // not signaled until Host.StartAsync ACTUALLY completes; if
            // registration has already completed, the callback runs
            // IMMEDIATELY (per the CancellationToken.Register contract).
            lifetime.ApplicationStarted.Register(lifetime.StopApplication);

            throw;
        }
    }
}
