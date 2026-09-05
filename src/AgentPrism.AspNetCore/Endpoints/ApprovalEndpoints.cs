using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>Asynchronous approval mailbox endpoints.</summary>
/// <remarks>
/// <para>
/// A pending approval request arises when a run driven from the queue
/// (<c>Prefer: respond-async</c>) closes with
/// <see cref="RunStatus.AwaitingApproval"/> (see <c>AgentRunJobHandler</c>). The
/// decision is made through <see cref="DecideAsync"/>; the old run row is NEVER
/// changed again (the same principle as <see cref="RunStatus.AwaitingInput"/>) —
/// the decision enqueues a NEW run (same <c>sessionId</c>, new <c>RunId</c>).
/// </para>
/// <para>
/// It is written to the audit trail BEFORE calling
/// <see cref="IPendingApprovalStore.DecideAsync"/>: the same exception
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
            .WithSummary("Lists the tenant's pending approval requests.")
            .WithDescription(
                "Only requests still awaiting a decision are returned; a decided request leaves " +
                "the list and stays readable by id. A request appears here when a queued run " +
                "('Prefer: respond-async') stops on a tool call that needs approval — a run " +
                "driven synchronously carries its approval in the response stream instead and " +
                "never reaches this mailbox. Each entry carries an expiry, which is an absolute " +
                "point in the future rather than an elapsed duration. If a registered " +
                "IRunAuthorizationHandler denies the caller, the response is 403 — the list is " +
                "REJECTED, never silently filtered.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        builder.MapGet("/api/approvals/{id:guid}", GetAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("AgentPrismGetPendingApproval")
            .WithTags("AgentPrism", "Approvals")
            .WithSummary("Returns a single pending approval request.")
            .WithDescription(
                "Unlike the list, this reads a request in any state, so it is how a client polls " +
                "the outcome after deciding: the response then carries who decided, when, and " +
                "which way. The request holds the tool call's arguments as recorded, which is " +
                "what an approver reviews before deciding. An unknown id returns 404, and so does a " +
                "denial by a registered IRunAuthorizationHandler.");

        builder.MapPost("/api/approvals/{id:guid}/decide", DecideAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismDecideApproval")
            .WithTags("AgentPrism", "Approvals")
            .WithSummary("Decides a pending approval request.")
            .Accepts<ApprovalDecisionRequest>("application/json")
            .WithDescription(
                "The decision enqueues a NEW run (same sessionId, new RunId); the old run " +
                "stays AwaitingApproval. A second decision on the same request gets 409. If a " +
                "registered IRunAuthorizationHandler denies the caller, the response is 404, " +
                "identical to an approval request that does not exist — the 409 is never reached, " +
                "so a denial cannot reveal that the request was already decided.");
    }

    private static async Task<Results<Ok<IReadOnlyList<PendingApproval>>, ProblemHttpResult>> ListPendingAsync(
        [FromServices] IPendingApprovalStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        CancellationToken cancellationToken)
    {
        // A denied list is REJECTED (403), never silently filtered: there is no
        // single approval identity to hide.
        if (await RunAuthorizationGate
                .CheckRunResourceAsync(
                    runAuthorizationHandler,
                    tenants,
                    runId: null,
                    agentName: null,
                    sessionId: null,
                    attributionContext,
                    RunAccess.Approval,
                    NotAuthorized(),
                    cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        var pending = await store.ListPendingAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(pending);
    }

    private static async Task<Results<Ok<PendingApproval>, ProblemHttpResult>> GetAsync(
        Guid id,
        [FromServices] IPendingApprovalStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        CancellationToken cancellationToken)
    {
        var approval = await store.GetAsync(id, cancellationToken).ConfigureAwait(false);

        if (approval is null)
        {
            return NotFound(id);
        }

        if (await RunAuthorizationGate
                .CheckRunResourceAsync(
                    runAuthorizationHandler,
                    tenants,
                    approval.RunId,
                    agentName: null,
                    approval.SessionId,
                    attributionContext,
                    RunAccess.Approval,
                    NotFound(id),
                    cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        return TypedResults.Ok(approval);
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
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
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

        // 🚨 Asked BEFORE the status is read: a 409 would tell a denied caller
        // that the request exists and has already been decided.
        if (await RunAuthorizationGate
                .CheckRunResourceAsync(
                    runAuthorizationHandler,
                    tenants,
                    approval.RunId,
                    agentName: null,
                    approval.SessionId,
                    attributionContext,
                    RunAccess.Approval,
                    NotFound(id),
                    cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
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

                // 🚨 Attribution is INHERITED from the run being resumed, not
                // read from the current request: the person who approved the
                // tool call is not the person whose budget the run spends. The
                // approver is recorded in the audit trail, which is where "who
                // decided" belongs.
                UserId = originalRun.UserId,
                Labels = originalRun.Labels,
                SessionId = approval.SessionId,
            },
            cancellationToken).ConfigureAwait(false);

        await jobStore.EnqueueAsync(
            new JobRecord
            {
                Id = newRunId,
                TenantId = tenants.TenantId,
                HandlerKey = JobHandlerKeys.ApprovalResume,
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
    /// exception (the SAME pattern as <c>SandboxedSkillScriptRunner.WriteAuditOrThrowAsync</c>).
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

    /// <summary>The response a denied LIST gets: 403, naming no approval request.</summary>
    private static ProblemHttpResult NotAuthorized()
        => TypedResults.Problem(
            title: "Run not authorized",
            detail: "The registered IRunAuthorizationHandler denied this request.",
            statusCode: StatusCodes.Status403Forbidden);

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
