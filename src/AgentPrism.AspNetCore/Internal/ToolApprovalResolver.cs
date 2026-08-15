using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Converts approval decisions from the UI into the response content the Microsoft
/// Agent Framework expects, and records "don't ask again" rules.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The approval is the next turn's input.</strong> MAF returns a pending
/// tool call in the response as <c>ToolApprovalRequestContent</c>, and expects the
/// decision in the next run's messages as <c>ToolApprovalResponseContent</c>. This
/// is why there is no separate "continue" endpoint; the decision is carried in the
/// body of the run request.
/// </para>
/// <para>
/// <strong>"Don't ask again" is kept in our own store, not in MAF's own format.</strong>
/// MAF offers <c>CreateAlwaysApproveToolResponse</c>, but it offers no place to
/// persist that decision; the rule would stay confined to process memory, could not
/// be scoped to a tenant, and could not be revoked from the UI. The rule is written
/// into <see cref="IToolApprovalRuleStore"/> and enforced by
/// <see cref="ToolApprovalRuleEvaluator"/> on subsequent runs.
/// Rationale: <c>docs/KARARLAR.md</c>, decision K-061.
/// </para>
/// </remarks>
internal static class ToolApprovalResolver
{
    /// <summary>
    /// Matches decisions against pending requests in the session history and
    /// produces the message to send.
    /// </summary>
    /// <param name="decisions">The decisions coming from the UI.</param>
    /// <param name="agent">The resolved agent.</param>
    /// <param name="agentName">The agent name. The persisted rule is scoped to this name.</param>
    /// <param name="session">The open session.</param>
    /// <param name="chatHistory">The chat history provider.</param>
    /// <param name="rules">The persisted rule store.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="auditLog">The audit log.</param>
    /// <param name="actorResolver">The actor resolver.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The message carrying the approval responses; <see langword="null"/> if no
    /// pending request matches.
    /// </returns>
    public static async ValueTask<ChatMessage?> BuildResponseMessageAsync(
        IReadOnlyList<ToolApprovalDecision> decisions,
        AIAgent agent,
        string agentName,
        AgentSession session,
        ChatHistoryProvider chatHistory,
        IToolApprovalRuleStore rules,
        ITenantContext tenantContext,
        IAuditLog auditLog,
        IAuditActorResolver actorResolver,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var history = await ChatHistoryReader
            .ReadAsync(agent, session, chatHistory, cancellationToken)
            .ConfigureAwait(false);

        var pending = CollectPendingRequests(history);

        if (pending.Count == 0)
        {
            return null;
        }

        var contents = new List<AIContent>(decisions.Count);

        foreach (var decision in decisions)
        {
            if (!pending.TryGetValue(decision.RequestId, out var request))
            {
                logger.LogWarning(
                    "No pending approval request with id '{RequestId}' was found; the decision was ignored.",
                    decision.RequestId);

                continue;
            }

            contents.Add(request.CreateResponse(decision.Approved, decision.Reason ?? string.Empty));

            var toolName = request.ToolCall is FunctionCallContent functionCall ? functionCall.Name : "unknown";

            await AuditRecorder.WriteAsync(
                auditLog,
                actorResolver,
                logger,
                tenantContext.TenantId,
                action: "approval.decision",
                entity: $"tool:{toolName}",
                before: null,
                after: JsonSerializer.Serialize(new { approved = decision.Approved, reason = decision.Reason }),
                cancellationToken).ConfigureAwait(false);

            if (decision is { Approved: true, Remember: true })
            {
                await RememberAsync(decision, request, agentName, rules, tenantContext, logger, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return contents.Count == 0 ? null : new ChatMessage(ChatRole.User, contents);
    }

    /// <summary>Collects the unanswered approval requests in the session history.</summary>
    /// <remarks>
    /// Answered requests are filtered out: sending a second response to the same
    /// request would give the model contradictory input.
    /// </remarks>
    private static Dictionary<string, ToolApprovalRequestContent> CollectPendingRequests(
        IReadOnlyList<ChatMessage> history)
    {
        var requests = new Dictionary<string, ToolApprovalRequestContent>(StringComparer.Ordinal);
        var answered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var message in history)
        {
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case ToolApprovalRequestContent request:
                        requests[request.RequestId] = request;
                        break;

                    case ToolApprovalResponseContent response:
                        answered.Add(response.RequestId);
                        break;

                    default:
                        break;
                }
            }
        }

        foreach (var requestId in answered)
        {
            requests.Remove(requestId);
        }

        return requests;
    }

    private static async ValueTask RememberAsync(
        ToolApprovalDecision decision,
        ToolApprovalRequestContent request,
        string agentName,
        IToolApprovalRuleStore rules,
        ITenantContext tenantContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (request.ToolCall is not FunctionCallContent call)
        {
            logger.LogWarning(
                "Request '{RequestId}' does not carry a function call; the persisted approval rule could not be written.",
                decision.RequestId);

            return;
        }

        try
        {
            await rules.AddAsync(
                new ToolApprovalRule
                {
                    Id = AgentPrismId.NewId(),
                    TenantId = tenantContext.TenantId,
                    AgentName = agentName,
                    ToolName = call.Name,
                    ArgumentsHash = decision.RememberArgumentsOnly
                        ? ToolApprovalRuleEvaluator.ComputeArgumentsHash(call.Arguments)
                        : null,
                    CreatedAt = DateTimeOffset.UtcNow,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Failing to write the rule does not interrupt the run: this turn's
            // approval was already given; only the next turn asks again.
            logger.LogWarning(
                ex,
                "The persisted approval rule for '{ToolName}' could not be written; approval will be asked for again on the next call.",
                call.Name);
        }
    }
}
