using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>Veri saklama politikasi ve arsivleme uclari (Faz 25).</summary>
/// <remarks>
/// <para>
/// 🚨 <c>preview</c> ucu zorunludur: kimse ne kadar veri sileceğini bilmeden
/// silme baslatmamalidir. Hicbir uc dogrudan silme yapmaz — <c>run</c> ucu
/// bile isi kuyruga yazar (<see cref="JobKind.Retention"/>), senkron calismaz.
/// </para>
/// <para>
/// Tum bagimliliklar <c>[FromServices]</c> ile acikca isaretlenir (Faz 9 dersi).
/// </para>
/// </remarks>
internal static class RetentionEndpoints
{
    /// <summary>Saklama uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/retention", ListAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismListRetentionPolicies")
            .WithSummary("Bir kiracinin saklama politikalarini listeler.");

        builder.MapGet("/api/retention/preview", PreviewAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismPreviewRetention")
            .WithSummary("Su an calistirilirsa kac satirin silinecegini gosterir. Silme YAPMAZ.");

        builder.MapPost("/api/retention/run", RunAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismRunRetention")
            .WithSummary("Temizlemeyi simdi calistirir.")
            .WithDescription("Senkron calismaz: bir JobKind.Retention isi kuyruga yazilir ve kuyrukta islenir.");

        builder.MapGet("/api/retention/history", HistoryAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismRetentionHistory")
            .WithSummary("Gecmis temizleme kosularini listeler.");

        builder.MapGet("/api/retention/{target}", GetAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismGetRetentionPolicy")
            .WithSummary("Tek bir hedefin saklama politikasini getirir.");

        builder.MapPut("/api/retention/{target}", SaveAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismSaveRetentionPolicy")
            .WithSummary("Bir hedef icin saklama politikasi olusturur veya gunceller.");

        builder.MapDelete("/api/retention/{target}", DeleteAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismDeleteRetentionPolicy")
            .WithSummary("Bir hedefin saklama politikasini siler.");
    }

    private static async Task<Ok<IReadOnlyList<RetentionPolicy>>> ListAsync(
        [FromServices] IRetentionPolicyStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var policies = await store.ListPoliciesAsync(tenants.TenantId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(policies);
    }

    private static async Task<Results<Ok<RetentionPolicy>, ProblemHttpResult>> GetAsync(
        string target,
        [FromServices] IRetentionPolicyStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        if (!RetentionTargets.IsKnown(target))
        {
            return UnknownTarget(target);
        }

        var policy = await store.GetPolicyAsync(tenants.TenantId, target, cancellationToken).ConfigureAwait(false);

        return policy is null ? NotFound(target) : TypedResults.Ok(policy);
    }

    private static async Task<Results<Ok<RetentionPolicy>, ProblemHttpResult>> SaveAsync(
        string target,
        [FromBody] RetentionPolicySaveRequest request,
        [FromServices] IRetentionPolicyStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] TimeProvider? timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!RetentionTargets.IsKnown(target))
        {
            return UnknownTarget(target);
        }

        if (request.MaxAgeDays is < 1)
        {
            return Invalid("'maxAgeDays' belirtiliyorsa en az 1 olmalidir.");
        }

        if (request.MaxRows is < 1)
        {
            return Invalid("'maxRows' belirtiliyorsa en az 1 olmalidir.");
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var previous = await store.GetPolicyAsync(tenants.TenantId, target, cancellationToken).ConfigureAwait(false);

        var saved = await store.SavePolicyAsync(
            new RetentionPolicy
            {
                Id = previous?.Id ?? AgentPrismId.NewId(),
                TenantId = tenants.TenantId,
                Target = target,
                MaxAgeDays = request.MaxAgeDays,
                MaxRows = request.MaxRows,
                Archive = request.Archive,
                Enabled = request.Enabled,
                CreatedAt = previous?.CreatedAt ?? now,
                UpdatedAt = now,
            },
            cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("AgentPrism.RetentionEndpoints"),
            tenants.TenantId,
            action: previous is null ? "retention.create" : "retention.update",
            entity: $"retention:{target}",
            before: previous is null ? null : Describe(previous),
            after: Describe(saved),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(saved);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        string target,
        [FromServices] IRetentionPolicyStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!RetentionTargets.IsKnown(target))
        {
            return UnknownTarget(target);
        }

        var existing = await store.GetPolicyAsync(tenants.TenantId, target, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return NotFound(target);
        }

        await store.DeletePolicyAsync(tenants.TenantId, target, cancellationToken).ConfigureAwait(false);

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("AgentPrism.RetentionEndpoints"),
            tenants.TenantId,
            action: "retention.delete",
            entity: $"retention:{target}",
            before: Describe(existing),
            after: null,
            cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<IReadOnlyList<RetentionPreview>>, ProblemHttpResult>> PreviewAsync(
        [FromQuery] string? target,
        [FromServices] RetentionExecutor executor,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        if (target is not null && !RetentionTargets.IsKnown(target))
        {
            return UnknownTarget(target);
        }

        var preview = await executor.PreviewAsync(tenants.TenantId, target, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(preview);
    }

    private static async Task<Results<Ok<RetentionRunTriggerResponse>, ProblemHttpResult>> RunAsync(
        [FromQuery] string? target,
        [FromServices] IJobStore jobStore,
        [FromServices] ITenantContext tenants,
        [FromServices] TimeProvider? timeProvider,
        CancellationToken cancellationToken)
    {
        if (target is not null && !RetentionTargets.IsKnown(target))
        {
            return UnknownTarget(target);
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var targetName = target ?? "*";

        var job = await jobStore.EnqueueAsync(
            new JobRecord
            {
                Id = AgentPrismId.NewId(),
                TenantId = tenants.TenantId,
                Kind = JobKind.Retention,
                TargetName = targetName,
                Status = JobStatus.Pending,
                Payload = JsonSerializer.SerializeToElement(new { target = targetName }),
                ScheduledFor = now,
                CreatedAt = now,
            },
            [targetName],
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new RetentionRunTriggerResponse { JobId = job.Id, Target = targetName });
    }

    private static async Task<Ok<IReadOnlyList<RetentionRun>>> HistoryAsync(
        [FromQuery] string? target,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        [FromServices] IRetentionPolicyStore store,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        var runs = await store.ListRunsAsync(
            tenants.TenantId,
            target,
            Math.Max(skip ?? 0, 0),
            Math.Clamp(take ?? 50, 1, 200),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(runs);
    }

    /// <summary>Bir politikayi denetim izi icin ozetler.</summary>
    private static string Describe(RetentionPolicy policy)
    {
        using var buffer = new MemoryStream();
        using var writer = new Utf8JsonWriter(buffer);

        writer.WriteStartObject();
        writer.WriteString("target", policy.Target);

        if (policy.MaxAgeDays is { } maxAgeDays)
        {
            writer.WriteNumber("maxAgeDays", maxAgeDays);
        }
        else
        {
            writer.WriteNull("maxAgeDays");
        }

        writer.WriteBoolean("archive", policy.Archive);
        writer.WriteBoolean("enabled", policy.Enabled);
        writer.WriteEndObject();
        writer.Flush();

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static ProblemHttpResult UnknownTarget(string target)
        => TypedResults.Problem(
            title: "Bilinmeyen hedef",
            detail: $"'{target}' taninan bir saklama hedefi degil. Gecerli hedefler: " +
                     $"{string.Join(", ", RetentionTargets.All)}.",
            statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult NotFound(string target)
        => TypedResults.Problem(
            title: "Politika bulunamadi",
            detail: $"'{target}' hedefi icin bir saklama politikasi yok.",
            statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult Invalid(string detail)
        => TypedResults.Problem(
            title: "Gecersiz politika",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>Bir saklama politikasini kaydetmek icin istek govdesi.</summary>
public sealed record RetentionPolicySaveRequest
{
    /// <summary>Bu yastan eski satirlar silinmeye adaydir.</summary>
    public int? MaxAgeDays { get; init; }

    /// <summary>Rezerve: hacim bazli kirpma icin (henuz uygulanmaz).</summary>
    public long? MaxRows { get; init; }

    /// <summary>Silmeden once arsivlensin mi.</summary>
    public bool Archive { get; init; }

    /// <summary>Politika etkin mi.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>Bir "simdi calistir" isteginin yaniti.</summary>
public sealed record RetentionRunTriggerResponse
{
    /// <summary>Kuyruga yazilan isin kimligi.</summary>
    public required Guid JobId { get; init; }

    /// <summary>Islenecek hedef; tumu icin <c>"*"</c>.</summary>
    public required string Target { get; init; }
}
