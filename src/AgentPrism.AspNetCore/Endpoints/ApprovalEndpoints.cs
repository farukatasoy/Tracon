using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>Asynchronous approval mailbox endpoints (Phase 55).</summary>
/// <remarks>
/// <para>
/// A pending approval request arises when a run driven from the queue
/// (<c>Prefer: respond-async</c>, Phase 46) closes with
/// <see cref="RunStatus.AwaitingApproval"/> (see <c>AgentRunJobHandler</c>). The
/// decision is made through <see cref="DecideAsync"/>; the old run row is NEVER
/// changed again (K-014, the same principle as <see cref="RunStatus.AwaitingInput"/>) —
/// the decision enqueues a NEW run (same <c>sessionId</c>, new <c>RunId</c>).
/// </para>
/// <para>
/// 🚨 It is written to the audit trail BEFORE calling
/// <see cref="IPendingApprovalStore.DecideAsync"/>: the same exception as K-089
/// ("an approval decision that cannot be written to the audit trail is not
/// applied"). If the write fails, the decision is never applied at all.
/// </para>
/// </remarks>
internal static class ApprovalEndpoints
{
    /// <summary>Maps the approval endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/approvals/pending", ListPendingAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("AgentPrismListPendingApprovals")
            .WithTags("AgentPrism", "Approvals")
            .WithSummary("Lists the tenant's pending approval requests.");

        builder.MapGet("/api/approvals/{id:guid}", GetAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("AgentPrismGetPendingApproval")
            .WithTags("AgentPrism", "Approvals")
            .WithSummary("Returns a single pending approval request.");

        builder.MapPost("/api/approvals/{id:guid}/decide", DecideAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismDecideApproval")
            .WithTags("AgentPrism", "Approvals")
            .WithSummary("Decides a pending approval request.")
            .Accepts<ApprovalDecisionRequest>("application/json")
            .WithDescription(
                "The decision enqueues a NEW run (same sessionId, new RunId); the old run " +
                "stays AwaitingApproval. A second decision on the same request gets 409.");
    }

    private static async Task<Ok<IReadOnlyList<PendingApproval>>> ListPendingAsync(
        [FromServices] IPendingApprovalStore store,
        CancellationToken cancellationToken)
    {
        var pending = await store.ListPendingAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(pending);
    }

    private static async Task<Results<Ok<PendingApproval>, ProblemHttpResult>> GetAsync(
        Guid id,
        [FromServices] IPendingApprovalStore store,
        CancellationToken cancellationToken)
    {
        var approval = await store.GetAsync(id, cancellationToken).ConfigureAwait(false);

        return approval is null ? NotFound(id) : TypedResults.Ok(approval);
    }

    private static async Task<Results<Ok<PendingApproval>, ProblemHttpResult>> DecideAsync(
        Guid id,
        HttpContext httpContext,
        [FromServices] IPendingApprovalStore approvals,
        [FromServices] IRunStore runStore,
        [FromServices] IJobStore jobStore,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<ApprovalDecisionRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        var approval = await approvals.GetAsync(id, cancellationToken).ConfigureAwait(false);

        if (approval is null)
        {
            return NotFound(id);
        }

        if (approval.Status != ApprovalStatus.Pending)
        {
            return AlreadyDecided(id);
        }

        var logger = loggerFactory.CreateLogger("AgentPrism.ApprovalEndpoints");
        var actor = actorResolver.Resolve() ?? "unknown";
        var now = DateTimeOffset.UtcNow;

        // 🚨 K-089: the audit trail entry is written BEFORE the decision. If the
        // write fails, the exception causes the caller to get 500 and DecideAsync
        // is NEVER called — "an approval decision that cannot be written to the
        // audit trail is not applied".
        await WriteAuditOrThrowAsync(
            auditLog,
            actorResolver,
            logger,
            tenants.TenantId,
            approval,
            request.Approved,
            cancellationToken).ConfigureAwait(false);

        var applied = await approvals
            .DecideAsync(id, request.Approved, actor, now, cancellationToken)
            .ConfigureAwait(false);

        if (!applied)
        {
            // A concurrent second decision (rare race). The audit trail already
            // correctly reflects the "attempted" decision just written; the DB's
            // WHERE status = Pending guard determines the true state.
            return AlreadyDecided(id);
        }

        // Regardless of the decision (approve/reject) the run is RESUMED: the model
        // must see a result or a rejection and proceed accordingly — the SAME
        // behavior as ToolApprovalResolver on the synchronous path.
        var originalRun = await runStore.GetRunAsync(approval.RunId, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException(
                $"There is no run with id '{approval.RunId}'; the 'pending_approvals.run_id' " +
                "foreign key should have made this impossible.");

        var newRunId = AgentPrismId.NewId();

        await runStore.StartRunAsync(
            new RunStartInfo
            {
                RunId = newRunId,
                AgentName = originalRun.AgentName,
                Status = RunStatus.Queued,
                StartedAt = now,
                TenantId = tenants.TenantId,
                SessionId = approval.SessionId,
            },
            cancellationToken).ConfigureAwait(false);

        await jobStore.EnqueueAsync(
            new JobRecord
            {
                Id = newRunId,
                TenantId = tenants.TenantId,
                Kind = JobKind.ApprovalResume,
                TargetName = originalRun.AgentName,
                Status = JobStatus.Pending,
                Payload = BuildResumePayload(newRunId, id, originalRun.AgentName),
                MaxAttempts = 1,
                ScheduledFor = now,
                CreatedAt = now,
            },
            [],
            cancellationToken).ConfigureAwait(false);

        var decided = await approvals.GetAsync(id, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(decided!);
    }

    /// <summary>Throws if the decision cannot be written to the audit trail; returns on a successful write.</summary>
    /// <remarks>
    /// UNLIKE <c>AuditRecorder.WriteAsync</c>, it does NOT swallow the error — the same
    /// exception as K-089 (the SAME pattern as <c>SandboxedSkillScriptRunner.WriteAuditOrThrowAsync</c>).
    /// </remarks>
    private static async ValueTask WriteAuditOrThrowAsync(
        IAuditLog auditLog,
        IAuditActorResolver actorResolver,
        ILogger logger,
        string tenantId,
        PendingApproval approval,
        bool decisionApproved,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditLog.WriteAsync(
                new AuditEntry
                {
                    Id = AgentPrismId.NewId(),
                    TenantId = tenantId,
                    Actor = actorResolver.Resolve(),
                    Action = "approval.decision",
                    Entity = $"tool:{approval.ToolName}",
                    Before = null,
                    After = AuditSecretFilter.Redact(
                        JsonSerializer.Serialize(new { approved = decisionApproved, approvalId = approval.Id })),
                    CreatedAt = DateTimeOffset.UtcNow,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Failed to write the approval decision with id '{ApprovalId}' to the audit trail; the decision was not applied.",
                approval.Id);

            throw new AgentPrismException(
                $"The approval decision with id '{approval.Id}' was not applied because it could not be written to the audit trail.",
                ex);
        }
    }

    private static JsonElement BuildResumePayload(Guid runId, Guid approvalId, string agentName)
        => JsonSerializer.SerializeToElement(new
        {
            runId = runId.ToString(),
            approvalId = approvalId.ToString(),
            agentName,
        });

    private static ProblemHttpResult NotFound(Guid id)
        => TypedResults.Problem(
            title: "Approval request not found",
            detail: $"There is no pending approval request with id '{id}', or it does not belong to this tenant.",
            statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult AlreadyDecided(Guid id)
        => TypedResults.Problem(
            title: "Decision already made",
            detail: $"The approval request with id '{id}' is no longer pending.",
            statusCode: StatusCodes.Status409Conflict);
}

/// <summary>Request body for deciding a pending approval request.</summary>
public sealed record ApprovalDecisionRequest
{
    /// <summary>Gets whether the request was approved.</summary>
    public required bool Approved { get; init; }
}
