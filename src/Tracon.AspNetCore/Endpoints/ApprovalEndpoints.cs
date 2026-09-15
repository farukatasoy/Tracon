using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Tracon;

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
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapGet("/api/approvals/pending", ListPendingAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconListPendingApprovals")
            .WithTags("Tracon", "Approvals")
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
            .WithName("TraconGetPendingApproval")
            .WithTags("Tracon", "Approvals")
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
            .WithName("TraconDecideApproval")
            .WithTags("Tracon", "Approvals")
            .WithSummary("Decides a pending approval request.")
            .Accepts<ApprovalDecisionRequest>("application/json")
            .WithDescription(
                "The decision enqueues a NEW run (same sessionId, new RunId); the old run " +
                "stays AwaitingApproval. Do not start that run yourself. Repeating the SAME " +
                "decision is safe and returns 200: the continuation run's identity is derived " +
                "from the approval, so a repeat finishes a handoff that failed partway instead " +
                "of creating a second run. A second decision asking for the OPPOSITE answer " +
                "gets 409. If a registered IRunAuthorizationHandler denies the caller, the " +
                "response is 404, identical to an approval request that does not exist — the " +
                "409 is never reached, so a denial cannot reveal that the request was already " +
                "decided.");
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
        [FromServices] TraconMetrics metrics,
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

        // 🚨 An approval that is no longer pending is not automatically a
        // conflict. Applying the decision and handing the run off to the worker
        // are separate writes with nothing spanning them, so a crash in between
        // leaves the approval decided with no run to carry it out. Answering
        // 409 there strands the decision permanently: the caller is told the
        // work is done when it never started, and nothing else recovers it
        // (run reconciliation only claims runs already Running, and is off by
        // default). Repeating the SAME decision therefore re-drives the
        // handoff; only a repeat that asks for the opposite answer conflicts.
        if (approval.Status != ApprovalStatus.Pending)
        {
            if (approval.Status != DecisionStatus(request.Approved))
            {
                return AlreadyDecided(id);
            }

            return await ResumeAsync(
                approval, runStore, jobStore, tenants, cancellationToken).ConfigureAwait(false);
        }

        var logger = loggerFactory.CreateLogger("Tracon.ApprovalEndpoints");
        var actor = actorResolver.Resolve() ?? "unknown";
        var now = DateTimeOffset.UtcNow;

        // 🚨 K-089: the audit trail entry is written BEFORE the decision. If the
        // write fails, the exception causes the caller to get 500 and DecideAsync
        // is NEVER called — "an approval decision that cannot be written to the
        // audit trail is not applied".
        await AuditRecorder.WriteOrThrowAsync(
            auditLog,
            actorResolver.Resolve(),
            logger,
            metrics,
            tenants.TenantId,
            action: "approval.decision",
            entity: $"tool:{approval.ToolName}",
            before: null,
            after: JsonSerializer.Serialize(
                new { approved = request.Approved, approvalId = approval.Id }),
            refusal: $"The approval decision with id '{approval.Id}' was not applied",
            timeProvider: null,
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

        var decided = await approvals.GetAsync(id, cancellationToken).ConfigureAwait(false) ?? approval;

        return await ResumeAsync(decided, runStore, jobStore, tenants, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The status a decision leaves the approval in.</summary>
    private static ApprovalStatus DecisionStatus(bool approved)
        => approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected;

    /// <summary>
    /// Creates the run that carries out a decision and queues it for the worker.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Regardless of the decision (approve/reject) the run is RESUMED: the model
    /// must see a result or a rejection and proceed accordingly — the SAME
    /// behavior as <c>ToolApprovalResolver</c> on the synchronous path.
    /// </para>
    /// <para>
    /// <strong>Safe to call more than once for the same approval.</strong> The run identifier
    /// is DERIVED from the approval rather than minted fresh, so a repeat lands
    /// on the same run and the same job instead of creating a second pair; each
    /// write is skipped when its row is already there. That is what lets a
    /// decision whose handoff was interrupted be finished by simply repeating
    /// it, with no table to remember the half-finished work.
    /// </para>
    /// <para>
    /// Not a transaction: two callers repeating the same decision at the same
    /// instant can both see a row missing and both try to write it, and one
    /// gets a duplicate-key error. That caller can retry — the identifiers are
    /// stable, so the state stays recoverable, which is the property the
    /// previous code lacked entirely.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<PendingApproval>, ProblemHttpResult>> ResumeAsync(
        PendingApproval approval,
        IRunStore runStore,
        IJobStore jobStore,
        ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var originalRun = await runStore.GetRunAsync(approval.RunId, cancellationToken).ConfigureAwait(false)
            ?? throw new TraconException(
                $"There is no run with id '{approval.RunId}'; the 'pending_approvals.run_id' " +
                "foreign key should have made this impossible.");

        // Both inputs are stored values, so every attempt derives the same id.
        var newRunId = TraconId.DeriveId(approval.CreatedAt, $"approval-resume:{approval.Id}");
        var at = approval.DecidedAt ?? DateTimeOffset.UtcNow;

        if (await runStore.GetRunAsync(newRunId, cancellationToken).ConfigureAwait(false) is null)
        {
            await runStore.StartRunAsync(
                new RunStartInfo
                {
                    RunId = newRunId,
                    AgentName = originalRun.AgentName,
                    Status = RunStatus.Queued,
                    StartedAt = at,
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
        }

        if (await jobStore.GetAsync(tenants.TenantId, newRunId, cancellationToken).ConfigureAwait(false) is null)
        {
            await jobStore.EnqueueAsync(
                new JobRecord
                {
                    Id = newRunId,
                    TenantId = tenants.TenantId,
                    HandlerKey = JobHandlerKeys.ApprovalResume,
                    TargetName = originalRun.AgentName,
                    Status = JobStatus.Pending,
                    Payload = BuildResumePayload(newRunId, approval.Id, originalRun.AgentName),
                    MaxAttempts = 1,
                    ScheduledFor = at,
                    CreatedAt = at,
                },
                [],
                cancellationToken).ConfigureAwait(false);
        }

        return TypedResults.Ok(approval);
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
