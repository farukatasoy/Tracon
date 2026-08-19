using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AgentPrism;

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
/// The call always starts as a NEW root run, with <see cref="AgentPrismRunOptions.Depth"/>
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
        var options = services.GetRequiredService<IOptionsMonitor<AgentPrismMcpServerOptions>>().CurrentValue;

        var toolName = request.Params?.Name;

        if (string.IsNullOrEmpty(toolName) ||
            !ExternalAgentToolNaming.TryParseAgentName(options.ToolNamePrefix, toolName, out var agentName) ||
            !ExternalSurfaceGuard.IsExposed(agentName, options.ExposedAgents, options.ExposeAllAgents))
        {
            return Error($"'{toolName}' is not a known AgentPrism agent tool.");
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

        var runOptions = new AgentPrismRunOptions
        {
            RunId = AgentPrismId.NewId(),
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
            return Error($"'{agentName}' could not be run: {ex.Message}");
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
