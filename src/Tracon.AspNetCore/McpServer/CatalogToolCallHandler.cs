using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Tracon;

/// <summary>
/// Turns a <c>tools/call</c> request into an actual run of an agent in the catalog.
/// </summary>
/// <remarks>
/// <para>
/// The agent is resolved with <see cref="IAgentCatalog.ResolveAsync(string, string, CancellationToken)"/>;
/// the returned agent is already wrapped with <c>RunRecordingAgent</c>, so this
/// call produces a normal <c>runs</c> row and normal quota consumption — a
/// second recording path is NOT written.
/// </para>
/// <para>
/// The call always starts as a NEW root run, with <see cref="TraconRunOptions.Depth"/>
/// 0. <c>ChildAgentInvoker</c> is NOT used here: it models one agent calling
/// ANOTHER agent and expects an ambient parent scope; an external caller has
/// no such scope.
/// </para>
/// </remarks>
internal static class CatalogToolCallHandler
{
    /// <summary>Handles a <c>tools/call</c> request.</summary>
    /// <param name="request">Request context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The agent's response, or a legible error result.</returns>
    public static async ValueTask<CallToolResult> HandleAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
        var services = CatalogToolListHandler.RequireServices(request);
        var options = services.GetRequiredService<IOptionsMonitor<TraconMcpServerOptions>>().CurrentValue;

        // 🚨 A task-mode call runs on a background Task.Run the SDK schedules
        // off the app's ROOT provider — no HttpContext, so the HTTP-based
        // ITenantContext falls back to the default tenant instead of the
        // caller's. Same shape as JobWorkerBackgroundService wrapping each
        // queued job in AmbientTenantScope.Begin(job.TenantId): the tenant
        // the ORIGINAL inbound request resolved is threaded through
        // McpTaskRunAmbient (captured before the SDK ever detaches from that
        // request) and re-asserted here. A real HttpContext (the synchronous
        // path) resolves correctly on its own and takes precedence over this
        // — see AmbientTenantScope's own contract — so this is a no-op there.
        using var tenantScope = McpTaskRunAmbient.Current is { } provisioning
            ? AmbientTenantScope.Begin(provisioning.TenantId)
            : null;

        var toolName = request.Params?.Name;

        if (string.IsNullOrEmpty(toolName) ||
            !ExternalAgentToolNaming.TryParseAgentName(options.ToolNamePrefix, toolName, out var agentName) ||
            !ExternalSurfaceGuard.IsExposed(agentName, options.ExposedAgents, options.ExposeAllAgents))
        {
            return Error($"'{toolName}' is not a known Tracon agent tool.");
        }

        var message = ExtractMessage(request.Params);

        if (string.IsNullOrWhiteSpace(message))
        {
            return Error("'message' argument cannot be empty.");
        }

        var catalog = services.GetRequiredService<IAgentCatalog>();
        var agent = await catalog.ResolveAsync(agentName, culture: null, cancellationToken).ConfigureAwait(false);

        if (agent is null)
        {
            return Error($"There is no agent named '{agentName}' in the catalog.");
        }

        var runOptions = new TraconRunOptions
        {
            // Reuses the id Tracon's task-provisioning filter already
            // minted for this call (Phase 117) when one is flowing — task
            // mode needs the run id fixed BEFORE this handler runs, since
            // RunBackedMcpTaskStore already promised it as the MCP task id.
            // Falls back to a fresh id so this handler still works when
            // called directly (tests) or outside that pipeline.
            RunId = McpTaskRunAmbient.Current?.RunId ?? TraconId.NewId(),
            Budget = new AgentRunBudget
            {
                MaxDepth = options.Budget.MaxDepth,
                MaxTotalTokens = options.Budget.MaxTotalTokens,
                MaxTotalRuns = options.Budget.MaxTotalRuns,
            },
        };

        AgentResponse response;

        try
        {
            response = await agent
                .RunAsync([new ChatMessage(ChatRole.User, message)], null, runOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var correlationId = SafeErrorText.NewCorrelationId();

            services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Tracon.McpServer")
                .LogError(ex, "MCP tool call for agent '{AgentName}' failed. (ref: {CorrelationId})", agentName, correlationId);

            return Error($"'{agentName}' could not be run: {SafeErrorText.ForPersistence(ex, correlationId)}");
        }

        // Defense layer: the startup check (ExternalSurfaceGuard) already
        // prevents exposing an agent carrying a tool that requires approval,
        // but the definition can be updated AFTERWARD to add an approval-
        // requiring tool (dynamic catalog). This is the runtime enforcement of
        // the same K-103 boundary.
        if (ChildRunApproval.Describe(response.Messages) is { } pending)
        {
            return Error(
                $"Agent '{agentName}' could not complete: tool '{pending}' requires user approval. " +
                "An externally-invoked agent cannot respond to an approval request.");
        }

        await ExternalCallAudit.WriteAsync(services, "mcp", agentName, runOptions.RunId!.Value, cancellationToken)
            .ConfigureAwait(false);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = response.Text }],
        };
    }

    private static string? ExtractMessage(CallToolRequestParams? parameters)
        => parameters?.Arguments is { } arguments &&
           arguments.TryGetValue("message", out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static CallToolResult Error(string message)
        => new()
        {
            IsError = true,
            Content = [new TextContentBlock { Text = message }],
        };
}
