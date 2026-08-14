using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AgentPrism;

/// <summary>
/// <c>tools/call</c> istegini katalogdaki bir agent'in gercek calistirmasina cevirir.
/// </summary>
/// <remarks>
/// <para>
/// Agent, <see cref="IAgentCatalog.ResolveAsync(string, CancellationToken)"/> ile
/// cozulur; donen agent zaten <c>RunRecordingAgent</c> ile sarilmistir, dolayisiyla
/// bu cagri normal bir <c>runs</c> satiri ve normal bir kota tuketimi uretir — ikinci
/// bir kayit yolu YAZILMAZ.
/// </para>
/// <para>
/// Cagri her zaman <see cref="AgentPrismRunOptions.Depth"/> 0 ile, YENI bir kok
/// calistirma olarak baslar. <c>ChildAgentInvoker</c> BURADA kullanilmaz: o, bir
/// agent'in BASKA bir agent'i cagirmasini modeller ve ambient bir ust kapsam ister;
/// dis cagiranin boyle bir kapsami yoktur.
/// </para>
/// </remarks>
internal static class CatalogToolCallHandler
{
    /// <summary><c>tools/call</c> isteğini isler.</summary>
    /// <param name="request">Istek baglami.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Agent yaniti veya anlasilir bir hata sonucu.</returns>
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
            return Error($"'{toolName}' bilinen bir AgentPrism agent tool'u degil.");
        }

        var message = ExtractMessage(request.Params);

        if (string.IsNullOrWhiteSpace(message))
        {
            return Error("'message' argumani bos olamaz.");
        }

        var catalog = services.GetRequiredService<IAgentCatalog>();
        var agent = await catalog.ResolveAsync(agentName, cancellationToken).ConfigureAwait(false);

        if (agent is null)
        {
            return Error($"'{agentName}' adinda bir agent katalogda yok.");
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

        // Defans katmani: acilis denetimi (ExternalSurfaceGuard) onayli tool tasiyan
        // bir agent'in disa acilmasini zaten engeller, ama tanim SONRADAN
        // guncellenip onayli bir tool eklenebilir (dinamik katalog). K-103'un ayni
        // sinirinin calisma anindaki ikinci uygulamasi.
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
