using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Extensions that connect the HTTP endpoint that publishes AgentPrism agents
/// as MCP tools.
/// </summary>
public static class AgentPrismMcpServerExtensions
{
    /// <summary>Default path.</summary>
    public const string DefaultPattern = "/agentprism/mcp";

    /// <summary>
    /// Publishes the exposed agents in the catalog as MCP tools.
    /// </summary>
    /// <param name="endpoints">The application's routing builder.</param>
    /// <param name="pattern">Path. Default is <c>/agentprism/mcp</c>.</param>
    /// <returns>The endpoint's convention builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="pattern"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// <c>UseMcpServer()</c> or <c>MapAgentPrism()</c> has not been called
    /// first, or this is called while remote access is enabled.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Applies the SAME three-layer protection as <c>MapAgentPrism</c>
    /// (loopback + bearer token + authorization policy) — settings are
    /// inherited from there, a second settings set is not built.
    /// </para>
    /// <para>
    /// 🚨 If <c>AllowRemoteAccess</c> is enabled this fails at startup. A
    /// single static token is not enough for an exposed agent surface
    /// (section 50.4).
    /// </para>
    /// <para>
    /// 🚨 If an exposed agent carries a tool that requires approval the
    /// application STILL fails — but not synchronously from this method,
    /// rather through <see cref="McpApprovalGuardFilter"/>: the check waits in
    /// the background until the SQL schema is ready (so this method itself
    /// does not crash with "no such table" against an empty database, the same
    /// pattern as K-354), completes by the first request, and no request can
    /// get ahead of it.
    /// </para>
    /// </remarks>
    public static IEndpointConventionBuilder MapAgentPrismMcpServer(
        this IEndpointRouteBuilder endpoints,
        string pattern = DefaultPattern)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var services = endpoints.ServiceProvider;
        var mcpOptionsMonitor = services.GetService<IOptionsMonitor<AgentPrismMcpServerOptions>>()
            ?? throw new InvalidOperationException(
                "MCP server services are not registered. Call builder.UseMcpServer(...) " +
                "before calling MapAgentPrismMcpServer().");

        var endpointOptions = AgentPrismEndpointRouteBuilderExtensions.RequireSharedEndpointOptions(endpoints, "MCP");

        ExternalSurfaceGuard.EnsureRemoteAccessNotCombined(
            endpointOptions.AllowRemoteAccess, "MCP", services.GetRequiredService<IApiKeyStore>());

        var approvalGuardFilter = new McpApprovalGuardFilter(
            services.GetRequiredService<SchemaReadyGate>(),
            services.GetRequiredService<IAgentCatalog>(),
            services.GetRequiredService<IToolRegistry>(),
            mcpOptionsMonitor,
            services.GetRequiredService<IHostApplicationLifetime>(),
            services.GetRequiredService<ILogger<McpApprovalGuardFilter>>());

        var group = endpoints.MapGroup(pattern).WithTags("AgentPrism", "MCP");
        group.AddEndpointFilter(approvalGuardFilter);
        group.AddEndpointFilter(new AgentPrismEndpointFilter(endpointOptions));
        group.RequireApiKeyScope(ApiKeyScope.ExternalInvoke);

        if (endpointOptions.AuthorizationPolicy is { Length: > 0 } policy)
        {
            group.RequireAuthorization(policy);
        }

        group.MapMcp();

        return group;
    }
}
