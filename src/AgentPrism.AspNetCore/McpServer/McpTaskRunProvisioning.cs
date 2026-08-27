using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AgentPrism;

/// <summary>The run id, agent name and tenant an MCP task-mode call was provisioned with.</summary>
/// <param name="RunId">
/// The run id <see cref="CatalogToolCallHandler"/> must reuse as
/// <c>AgentPrismRunOptions.RunId</c>, and the same value
/// <see cref="RunBackedMcpTaskStore"/> hands out as the MCP task id.
/// </param>
/// <param name="AgentName">Best-effort parsed agent name, for the placeholder run row.</param>
/// <param name="TenantId">The calling tenant, resolved from the original inbound request.</param>
internal sealed record McpTaskRunProvisioning(Guid RunId, string AgentName, string TenantId);

/// <summary>
/// Carries <see cref="McpTaskRunProvisioning"/> from the pre-Tasks call-tool filter
/// down into the SDK's Tasks extension and, for a task-mode call, into the
/// background execution it schedules.
/// </summary>
/// <remarks>
/// <para>
/// <c>IMcpTaskStore.CreateTaskAsync</c> takes only a <c>CancellationToken</c> — the
/// SDK's Tasks extension gives it no way to learn which tool is being called. The
/// only reliable channel left is an ambient value set by an ANCESTOR before it
/// calls into the SDK's own filter chain: <see cref="AsyncLocal{T}"/> flows
/// downward into everything an ancestor calls, including a nested
/// <c>Task.Run(...)</c>, as long as nothing between them suppresses execution
/// context flow (measured empirically before relying on it). It does NOT flow
/// back up to the caller once the nested call returns; the filter clears it
/// in a <c>finally</c> for that reason, not to work around a leak.
/// </para>
/// </remarks>
internal static class McpTaskRunAmbient
{
    private static readonly AsyncLocal<McpTaskRunProvisioning?> Storage = new();

    /// <summary>The provisioning for the call currently flowing through this async context, or <see langword="null"/>.</summary>
    public static McpTaskRunProvisioning? Current => Storage.Value;

    /// <summary>
    /// Sets the ambient provisioning. A setter method, not a property, to match
    /// <c>AgentPrismRunContext.SetCurrent</c>'s shape — the same name the
    /// ambient-write-site ratchet (<c>AmbientWriteSiteTests</c>) scans for.
    /// </summary>
    public static void SetCurrent(McpTaskRunProvisioning? value) => Storage.Value = value;
}

/// <summary>
/// Registers the pre-Tasks call-tool filter that provisions a run id (and, for
/// task-mode calls, opens the placeholder run row) before the SDK's own Tasks
/// filter decides whether to run the call synchronously or in the background.
/// </summary>
/// <remarks>
/// Runs for every <c>tools/call</c>, task-mode or not: it is registered before
/// <c>.WithTasks(...)</c>'s own filter, so an alternate-result filter ahead of it
/// runs before task creation (documented on
/// <c>McpRequestFilters.CallToolWithAlternateFilters</c>). Its effect is
/// value-transparent for the synchronous path — it hands out the same kind of
/// fresh id <see cref="CatalogToolCallHandler"/> would have minted itself — so
/// <c>EnableTasks = false</c> stays bit-for-bit identical to the synchronous-only behavior.
/// </remarks>
#pragma warning disable MCPEXP002
internal static class McpTaskRunProvisioningFilter
{
    public static void Register(McpServerOptions options)
        => options.Filters.Request.CallToolWithAlternateFilters.Insert(0, FilterAsync);

    private static async ValueTask<ResultOrAlternate<CallToolResult>> FilterAsync(
        RequestContext<CallToolRequestParams> context,
        McpRequestHandler<CallToolRequestParams, ResultOrAlternate<CallToolResult>> next,
        CancellationToken cancellationToken)
    {
        var services = CatalogToolListHandler.RequireServices(context);
        var mcpOptions = services.GetRequiredService<IOptionsMonitor<AgentPrismMcpServerOptions>>().CurrentValue;
        var toolName = context.Params?.Name;

        var agentName = "";

        if (toolName is not null)
        {
            ExternalAgentToolNaming.TryParseAgentName(mcpOptions.ToolNamePrefix, toolName, out agentName);
        }

        var tenantId = services.GetRequiredService<ITenantContext>().TenantId;
        var previous = McpTaskRunAmbient.Current;

        McpTaskRunAmbient.SetCurrent(new McpTaskRunProvisioning(AgentPrismId.NewId(), agentName, tenantId));

        try
        {
            return await next(context, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Restored, not just cleared: a filter ahead of THIS one on some
            // other call-tool pipeline (there is none today, but the next
            // reader should not have to re-derive that) may depend on its own
            // value still being current after this call returns.
            McpTaskRunAmbient.SetCurrent(previous);
        }
    }
}
#pragma warning restore MCPEXP002
