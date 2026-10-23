using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Records a tool approval decision: the audit row and the "do not ask again" rule.
/// </summary>
/// <remarks>
/// <para>
/// Two surfaces carry a decision to a tool call that waits for approval: the next run's request body
/// (<see cref="ToolApprovalResolver"/>) and the answer to a workflow's pending request
/// (<c>POST /api/workflows/runs/{runId}/respond</c>). Both write the SAME audit row and the
/// SAME rule, so the two writes live here once; a decision does not mean something different because of the
/// way it arrived.
/// </para>
/// <para>
/// The audit row comes BEFORE the decision is applied and a failed write throws, so a decision that cannot be
/// recorded is not carried out. The rule is best effort: a rule that cannot be written costs only the next
/// question.
/// </para>
/// </remarks>
internal static class ToolApprovalDecisionRecorder
{
    /// <summary>The audit action of a tool approval decision.</summary>
    public const string AuditAction = "approval.decision";

    /// <summary>
    /// Microsoft Agent Framework's own script dispatcher. Every skill script call
    /// arrives under this single tool name.
    /// </summary>
    private const string ScriptDispatcherToolName = "run_skill_script";

    // 🚨 A remembered rule for this tool is ALWAYS bound to the arguments. Every
    // script call shares this one tool name, so an argument-independent rule
    // auto-approved the agent's every future script - a different skill and a
    // different script included. Worse, that rule was reachable through the run
    // endpoint with Operator plus runs:write, while writing the same rule
    // directly requires Admin plus security:admin. "Do not ask again" now means
    // "do not ask again for THIS script with THESE arguments".

    /// <summary>Writes the audit row of a decision; throws when the row cannot be written.</summary>
    /// <param name="auditLog">The audit log.</param>
    /// <param name="actor">Who decided.</param>
    /// <param name="logger">The logger the failure is written to.</param>
    /// <param name="metrics">The metric set that counts a failed audit write.</param>
    /// <param name="tenantId">The tenant of the decision.</param>
    /// <param name="toolName">The tool the decision is about.</param>
    /// <param name="approved">Whether the call is approved.</param>
    /// <param name="reason">The reason the decision gave.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="surface">
    /// The surface that carried the decision, such as <c>ag-ui</c>; <see langword="null"/> for the surfaces whose
    /// row never named one, so their rows keep their shape.
    /// </param>
    /// <exception cref="TraconException">The row could not be written; the decision must not be applied.</exception>
    public static ValueTask WriteAuditAsync(
        IAuditLog auditLog,
        string? actor,
        ILogger logger,
        TraconMetrics? metrics,
        string tenantId,
        string toolName,
        bool approved,
        string? reason,
        CancellationToken cancellationToken,
        string? surface = null)
        => AuditRecorder.WriteOrThrowAsync(
            auditLog,
            actor,
            logger,
            metrics,
            tenantId,
            action: AuditAction,
            entity: $"tool:{toolName}",
            before: null,
            after: surface is null
                ? JsonSerializer.Serialize(new { approved, reason })
                : JsonSerializer.Serialize(new { approved, reason, surface }),
            refusal: $"The approval decision for tool '{toolName}' was not applied",
            timeProvider: null,
            cancellationToken);

    /// <summary>
    /// Saves an approved call as a standing rule ("do not ask again") for the agent that made it.
    /// </summary>
    /// <param name="rules">The rule store.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="tools">The tool registry; the rule records the tool's source from it.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="agentName">The agent the rule holds for.</param>
    /// <param name="toolName">The tool the rule holds for.</param>
    /// <param name="argumentsHash">
    /// The hash of the approved call's arguments (<see cref="ToolApprovalRuleEvaluator.ComputeArgumentsHash"/>);
    /// <see langword="null"/> when the caller does not know it.
    /// </param>
    /// <param name="rememberArgumentsOnly">Whether the rule covers only a call with the same arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the rule is written or the reason it was not is logged.</returns>
    public static async ValueTask RememberAsync(
        IToolApprovalRuleStore rules,
        ITenantContext tenantContext,
        IToolRegistry tools,
        ILogger logger,
        string agentName,
        string toolName,
        string? argumentsHash,
        bool rememberArgumentsOnly,
        CancellationToken cancellationToken)
    {
        var bindToArguments = rememberArgumentsOnly || IsScriptDispatcher(toolName);

        if (bindToArguments && argumentsHash is null)
        {
            // A rule that has to be bound to the arguments but cannot be would cover every call.
            logger.LogWarning(
                "The arguments of '{ToolName}' are not known; the persisted approval rule was not written.",
                toolName);

            return;
        }

        var sourceFingerprint = ToolSourceFingerprint.Of(tools, tenantContext.TenantId, toolName);

        if (!ToolSourceFingerprint.IsRecordable(sourceFingerprint))
        {
            // The server's tools are not discovered right now, so there is no source
            // to bind the rule to. This turn's approval stands; the next call asks again.
            logger.LogWarning(
                "The source of '{ToolName}' is not discovered right now; the persisted approval rule was not written.",
                toolName);

            return;
        }

        try
        {
            await rules.AddAsync(
                new ToolApprovalRule
                {
                    Id = TraconId.NewId(),
                    TenantId = tenantContext.TenantId,
                    AgentName = agentName,
                    ToolName = toolName,
                    ArgumentsHash = bindToArguments ? argumentsHash : null,

                    // The same source binding as an admin-written rule: "do not ask
                    // again" holds for this tool of THIS endpoint only.
                    SourceFingerprint = sourceFingerprint,
                    CreatedAt = DateTimeOffset.UtcNow,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            // Failing to write the rule does not interrupt the run: this turn's
            // approval was already given; only the next turn asks again.
            logger.LogWarning(
                ex,
                "The persisted approval rule for '{ToolName}' could not be written; approval will be asked for again on the next call.",
                toolName);
        }
    }

    private static bool IsScriptDispatcher(string toolName)
        => string.Equals(toolName, ScriptDispatcherToolName, StringComparison.Ordinal);
}
