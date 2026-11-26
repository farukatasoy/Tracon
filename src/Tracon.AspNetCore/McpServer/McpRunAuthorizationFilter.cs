using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Tracon;

/// <summary>
/// Asks the installation's <see cref="IRunAuthorizationHandler"/> whether a caller may start the agent a
/// <c>tools/call</c> names, before anything of the run exists.
/// </summary>
/// <remarks>
/// <para>
/// The same question every other run surface asks (<see cref="RunAuthorizationGate"/>): tenant, agent and the attributed
/// user. An MCP tool call is sessionless, so the session is <see langword="null"/>. A refused call answers the tool result
/// every other refusal on this surface is — <c>isError</c> set and one line of text — and nothing is recorded or called.
/// </para>
/// <para>
/// It runs as a call-tool filter, ahead of the SDK's Tasks filter, and not inside <see cref="CatalogToolCallHandler"/>:
/// a task-mode call runs that handler on a background task the SDK schedules off the application's root provider, with no
/// request, so the caller's identity is only there on the request that carries the call. Asking in the handler would ask
/// a task-mode call about nobody. Asked here, a refused call also creates no task and no placeholder run row.
/// </para>
/// <para>
/// A tool name that is not an exposed agent is not asked about: the handler decides who may start an agent, not whether
/// a tool exists, and the call falls through to the handler's own "not a known tool" answer. A draining process is not
/// asked either; the handler refuses the call first, before anything else, as it always did.
/// </para>
/// </remarks>
#pragma warning disable MCPEXP002
internal static class McpRunAuthorizationFilter
{
    /// <summary>Puts the filter first in the call-tool pipeline.</summary>
    /// <param name="options">The MCP server options.</param>
    public static void Register(McpServerOptions options)
        => options.Filters.Request.CallToolWithAlternateFilters.Insert(0, FilterAsync);

    private static async ValueTask<ResultOrAlternate<CallToolResult>> FilterAsync(
        RequestContext<CallToolRequestParams> context,
        McpRequestHandler<CallToolRequestParams, ResultOrAlternate<CallToolResult>> next,
        CancellationToken cancellationToken)
    {
        var services = CatalogToolListHandler.RequireServices(context);
        var options = services.GetRequiredService<IOptionsMonitor<TraconMcpServerOptions>>().CurrentValue;

        if (context.Params?.Name is { Length: > 0 } toolName
            && ExternalAgentToolNaming.TryParseAgentName(options.ToolNamePrefix, toolName, out var agentName)
            && ExternalSurfaceGuard.IsExposed(agentName, options.ExposedAgents, options.ExposeAllAgents)
            && !services.GetRequiredService<ITraconDrainState>().IsDraining
            && await AuthorizeAsync(services, agentName, cancellationToken).ConfigureAwait(false) is { } refusal)
        {
            return new ResultOrAlternate<CallToolResult>(refusal);
        }

        return await next(context, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<CallToolResult?> AuthorizeAsync(
        IServiceProvider services,
        string agentName,
        CancellationToken cancellationToken)
    {
        // The attribution limits hold on every surface that starts a run, with or without a handler: an oversized label set
        // is refused here, as the management API refuses it with a 400, instead of being dropped quietly when the run is
        // recorded.
        if (RunAttributionGate.Validate(services.GetService<IRunAttributionContext>()) is { } invalid)
        {
            return CatalogToolCallHandler.Error(invalid);
        }

        var handler = services.GetService<IRunAuthorizationHandler>();

        if (handler is null)
        {
            return null;
        }

        // The MCP library hands this filter the request's own services; a call that did not arrive over HTTP has no caller
        // to name, and a handler cannot decide about nobody.
        var httpContext = services.GetRequiredService<IHttpContextAccessor>().HttpContext
            ?? throw new InvalidOperationException(
                "The MCP request carries no HTTP request. MapTraconMcpServer only works over HTTP transport.");

        var refusal = await RunAuthorizationGate
            .CheckRunAsync(
                handler,
                services.GetRequiredService<ITenantContext>(),
                RunChannel.Mcp,
                agentName,
                sessionId: null,
                services.GetService<IRunAttributionContext>(),
                httpContext,
                cancellationToken)
            .ConfigureAwait(false);

        return refusal is null ? null : CatalogToolCallHandler.Error(RunAuthorizationGate.RefusalText(refusal));
    }
}
#pragma warning restore MCPEXP002
