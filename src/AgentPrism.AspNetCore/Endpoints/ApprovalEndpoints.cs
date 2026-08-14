using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>Asenkron onay kutusu uclari (Faz 55).</summary>
/// <remarks>
/// <para>
/// Bekleyen bir onay istegi, kuyruktan kosan (<c>Prefer: respond-async</c>,
/// Faz 46) bir calistirmanin <see cref="RunStatus.AwaitingApproval"/> ile
/// kapanmasindan dogar (bkz. <c>AgentRunJobHandler</c>). Karar
/// <see cref="DecideAsync"/> ile verilir; eski calistirma satiri BIR DAHA
/// DEGISMEZ (K-014, <see cref="RunStatus.AwaitingInput"/> ile ayni ilke) —
/// karar YENI bir calistirmayi (ayni <c>sessionId</c>, yeni <c>RunId</c>)
/// kuyruga dusurur.
/// </para>
/// <para>
/// 🚨 <see cref="IPendingApprovalStore.DecideAsync"/> cagirmadan ONCE denetim
/// izine yazilir: K-089'un ayni istisnasi ("denetim izine yazilamayan bir
/// onay kararı uygulanmaz"). Yazma basarisiz olursa karar hic uygulanmaz.
/// </para>
/// </remarks>
internal static class ApprovalEndpoints
{
    /// <summary>Onay uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/approvals/pending", ListPendingAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("AgentPrismListPendingApprovals")
            .WithTags("AgentPrism", "Approvals")
            .WithSummary("Kiracinin bekleyen onay isteklerini listeler.");

        builder.MapGet("/api/approvals/{id:guid}", GetAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("AgentPrismGetPendingApproval")
            .WithTags("AgentPrism", "Approvals")
            .WithSummary("Tek bir bekleyen onay istegini getirir.");

        builder.MapPost("/api/approvals/{id:guid}/decide", DecideAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismDecideApproval")
            .WithTags("AgentPrism", "Approvals")
            .WithSummary("Bekleyen bir onay istegine karar verir.")
            .Accepts<ApprovalDecisionRequest>("application/json")
            .WithDescription(
                "Karar YENI bir calistirma kuyruga dusurur (ayni sessionId, yeni RunId); " +
                "eski calistirma AwaitingApproval olarak kalir. Ayni istege ikinci karar 409 alir.");
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

        // 🚨 K-089: denetim izi karardan ONCE yazilir. Yazma basarisiz olursa
        // istisna cagiranin 500 almasina yol acar ve DecideAsync HIC cagrilmaz —
        // "denetim izine yazilamayan bir onay kararı uygulanmaz".
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
            // Es zamanli ikinci bir karar (nadir yaris). Denetim izi az once
            // yazilmis "denenen" karari dogru sekilde yansitir; DB'nin
            // WHERE status = Pending korumasi gercek durumu belirler.
            return AlreadyDecided(id);
        }

        // Karar ne olursa olsun (onay/ret) calistirma SURDURULUR: model bir
        // sonucu ya da bir reddi gormeli ve buna gore devam etmelidir — senkron
        // yoldaki ToolApprovalResolver ile AYNI davranis.
        var originalRun = await runStore.GetRunAsync(approval.RunId, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException(
                $"'{approval.RunId}' kimlikli calistirma bulunamadi; 'pending_approvals.run_id' " +
                "yabanci anahtari bunu imkansiz saymaliydi.");

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

    /// <summary>Karar denetim izine yazilamazsa firlatir; basarili yazimda doner.</summary>
    /// <remarks>
    /// <c>AuditRecorder.WriteAsync</c>'in AKSINE hatayi YUTMAZ — K-089'un ayni
    /// istisnasi (<c>SandboxedSkillScriptRunner.WriteAuditOrThrowAsync</c> ile
    /// AYNI desen).
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
                "'{ApprovalId}' kimlikli onay karari denetim izine yazilamadi; karar uygulanmadi.",
                approval.Id);

            throw new AgentPrismException(
                $"'{approval.Id}' kimlikli onay karari denetim izine yazilamadigi icin uygulanmadi.",
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

/// <summary>Bekleyen bir onay istegine karar vermek icin istek govdesi.</summary>
public sealed record ApprovalDecisionRequest
{
    /// <summary>Istek onaylandi mi.</summary>
    public required bool Approved { get; init; }
}
