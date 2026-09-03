using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>Data retention policy and archiving endpoints.</summary>
/// <remarks>
/// <para>
/// The <c>preview</c> endpoint is mandatory: no one should start a deletion
/// without knowing how much data it will delete. No endpoint deletes directly
/// — even the <c>run</c> endpoint enqueues a job (<see cref="JobHandlerKeys.Retention"/>);
/// it does not run synchronously.
/// </para>
/// <para>
/// All dependencies are explicitly marked with <c>[FromServices]</c>.
/// </para>
/// </remarks>
internal static class RetentionEndpoints
{
    /// <summary>Maps the retention endpoints.</summary>
    /// <param name="builder">The endpoint route builder.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/retention", ListAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("AgentPrismListRetentionPolicies")
            .WithTags("AgentPrism", "Retention")
            .WithSummary("Lists a tenant's retention policies.")
            .WithDescription(
                "Only targets that have an explicit policy appear here. A target missing from " +
                "the list is not cleaned up at all — absence means 'keep forever', not 'use a " +
                "default'. A policy is also kept while switched off, so 'enabled: false' is a " +
                "configured-but-paused policy and is different from having none.");

        builder.MapGet("/api/retention/preview", PreviewAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("AgentPrismPreviewRetention")
            .WithTags("AgentPrism", "Retention")
            .WithSummary("Shows how many rows would be deleted if run now. Does NOT delete.")
            .WithDescription(
                "Run this before every cleanup: it is the only way to see the size of a deletion " +
                "before it happens. The counts are computed against the data as it is right now, " +
                "so they are an estimate — rows written between the preview and the run are " +
                "included by the run. Without '?target=' every configured target is previewed; " +
                "an unknown target name returns 400. Nothing is written and no job is queued.");

        builder.MapPost("/api/retention/run", RunAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .WithName("AgentPrismRunRetention")
            .WithTags("AgentPrism", "Retention")
            .WithSummary("Runs the cleanup now.")
            .WithDescription("Does not run synchronously: an agentprism.retention job is enqueued and processed from the queue.");

        builder.MapGet("/api/retention/history", HistoryAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("AgentPrismRetentionHistory")
            .WithTags("AgentPrism", "Retention")
            .WithSummary("Lists past cleanup runs.")
            .WithDescription(
                "Each entry records one executed cleanup — the target, when it ran, and how many " +
                "rows it removed — which is how a deletion is accounted for after the fact. " +
                "Filter to one target with '?target='. Paging is offset based: 'skip' defaults " +
                "to 0, 'take' to 50, and 'take' is clamped to 1..200 instead of being rejected. " +
                "This history is not itself cleaned up by any policy — it is the permanent " +
                "record of what was deleted.");

        builder.MapGet("/api/retention/{target}", GetAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("AgentPrismGetRetentionPolicy")
            .WithTags("AgentPrism", "Retention")
            .WithSummary("Gets the retention policy for a single target.")
            .WithDescription(
                "Two different failures are reported differently: an unrecognized target name " +
                "returns 400 and lists the valid targets, while a valid target with no policy " +
                "configured returns 404. Read that 404 as 'this data is never cleaned up'.");

        builder.MapPut("/api/retention/{target}", SaveAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .WithName("AgentPrismSaveRetentionPolicy")
            .WithTags("AgentPrism", "Retention")
            .WithSummary("Creates or updates the retention policy for a target.")
            .WithDescription(
                "'maxAgeDays' and 'maxRows' are independent limits and both may be set; each " +
                "must be at least 1 when given, and leaving both unset means the policy removes " +
                "nothing. Saving does not delete anything by itself — the cleanup runs from the " +
                "queue, so preview first. Every save is written to the audit trail with the " +
                "previous and the new values. An unrecognized target returns 400.")
            .Accepts<RetentionPolicySaveRequest>("application/json");

        builder.MapDelete("/api/retention/{target}", DeleteAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .WithName("AgentPrismDeleteRetentionPolicy")
            .WithTags("AgentPrism", "Retention")
            .WithSummary("Deletes the retention policy for a target.")
            .WithDescription(
                "Removing a policy stops the cleanup for that target; it deletes no data and " +
                "restores none that was already deleted. To pause a cleanup while keeping the " +
                "limits, save the policy with 'enabled: false' instead. The removal is written " +
                "to the audit trail. An unrecognized target returns 400, a target with no " +
                "policy returns 404.");
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
        HttpContext httpContext,
        [FromServices] IRetentionPolicyStore store,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] TimeProvider? timeProvider,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<RetentionPolicySaveRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        if (!RetentionTargets.IsKnown(target))
        {
            return UnknownTarget(target);
        }

        if (request.MaxAgeDays is < 1)
        {
            return Invalid("'maxAgeDays' must be at least 1 if given.");
        }

        if (request.MaxRows is < 1)
        {
            return Invalid("'maxRows' must be at least 1 if given.");
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
                HandlerKey = JobHandlerKeys.Retention,
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

    /// <summary>Summarizes a policy for the audit trail.</summary>
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

        if (policy.MaxRows is { } maxRows)
        {
            writer.WriteNumber("maxRows", maxRows);
        }
        else
        {
            writer.WriteNull("maxRows");
        }

        writer.WriteBoolean("archive", policy.Archive);
        writer.WriteBoolean("enabled", policy.Enabled);
        writer.WriteEndObject();
        writer.Flush();

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static ProblemHttpResult UnknownTarget(string target)
        => TypedResults.Problem(
            title: "Unknown target",
            detail: $"'{target}' is not a recognized retention target. Valid targets: " +
                     $"{string.Join(", ", RetentionTargets.All)}.",
            statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult NotFound(string target)
        => TypedResults.Problem(
            title: "Policy not found",
            detail: $"There is no retention policy for target '{target}'.",
            statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult Invalid(string detail)
        => TypedResults.Problem(
            title: "Policy invalid",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>Request body for saving a retention policy.</summary>
public sealed record RetentionPolicySaveRequest
{
    /// <summary>Rows older than this age are candidates for deletion.</summary>
    public int? MaxAgeDays { get; init; }

    /// <summary>The maximum number of rows to keep for the target. The oldest rows are deleted.</summary>
    public long? MaxRows { get; init; }

    /// <summary>Whether to archive rows before deleting them.</summary>
    public bool Archive { get; init; }

    /// <summary>Whether the policy is enabled.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>Response to a "run now" request.</summary>
public sealed record RetentionRunTriggerResponse
{
    /// <summary>The id of the enqueued job.</summary>
    public required Guid JobId { get; init; }

    /// <summary>The target to process; <c>"*"</c> for all targets.</summary>
    public required string Target { get; init; }
}
