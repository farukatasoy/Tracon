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
    /// If <c>AllowRemoteAccess</c> is enabled this fails at startup. A
    /// single static token is not enough for an exposed agent surface.
    /// </para>
    /// <para>
    /// If an exposed agent carries a tool that requires approval the
    /// application STILL fails — but not synchronously from this method,
    /// rather through <see cref="McpApprovalGuardFilter"/>: the check waits in
    /// the background until the SQL schema is ready (so this method itself
    /// does not crash with "no such table" against an empty database, the same
    /// pattern), completes by the first request, and no request can
    /// get ahead of it.
    /// </para>
    /// <example>
    /// <code>
    /// app.MapAgentPrism();
    /// app.MapAgentPrismMcpServer();
    /// </code>
    /// </example>
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
        // 🚨 Order matters and this one is deliberate: authentication runs FIRST.
        // The approval guard awaits a startup check that has no timeout and no
        // retry limit (only ApplicationStopping cancels it), so with
        // AutoApplyMigrations off and the migration step not yet run, an
        // UNAUTHENTICATED request used to park on that task instead of getting
        // 401 - holding a connection and a thread-pool continuation each. A
        // caller must clear the door before it is allowed to wait in the hall.
        group.AddEndpointFilter(new AgentPrismEndpointFilter(endpointOptions));
        group.AddEndpointFilter(approvalGuardFilter);
        group.RequireApiKeyScope(ApiKeyScope.ExternalInvoke, mandatory: true);

        if (endpointOptions.AuthorizationPolicy is { Length: > 0 } policy)
        {
            group.RequireAuthorization(policy);
        }

        group.MapMcp();

        return group;
    }
}
