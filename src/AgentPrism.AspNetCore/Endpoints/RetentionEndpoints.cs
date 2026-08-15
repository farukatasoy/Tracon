using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>Data retention policy and archiving endpoints (Phase 25).</summary>
/// <remarks>
/// <para>
/// 🚨 The <c>preview</c> endpoint is mandatory: no one should start a deletion
/// without knowing how much data it will delete. No endpoint deletes directly
/// — even the <c>run</c> endpoint enqueues a job (<see cref="JobKind.Retention"/>);
/// it does not run synchronously.
/// </para>
/// <para>
/// All dependencies are explicitly marked with <c>[FromServices]</c> (a lesson
/// from Phase 9).
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
            .WithSummary("Lists a tenant's retention policies.");

        builder.MapGet("/api/retention/preview", PreviewAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("AgentPrismPreviewRetention")
            .WithTags("AgentPrism", "Retention")
            .WithSummary("Shows how many rows would be deleted if run now. Does NOT delete.");

        builder.MapPost("/api/retention/run", RunAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .WithName("AgentPrismRunRetention")
            .WithTags("AgentPrism", "Retention")
            .WithSummary("Runs the cleanup now.")
            .WithDescription("Does not run synchronously: a JobKind.Retention job is enqueued and processed from the queue.");

        builder.MapGet("/api/retention/history", HistoryAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("AgentPrismRetentionHistory")
            .WithTags("AgentPrism", "Retention")
            .WithSummary("Lists past cleanup runs.");

        builder.MapGet("/api/retention/{target}", GetAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformRead)
            .WithName("AgentPrismGetRetentionPolicy")
            .WithTags("AgentPrism", "Retention")
            .WithSummary("Gets the retention policy for a single target.");

        builder.MapPut("/api/retention/{target}", SaveAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .WithName("AgentPrismSaveRetentionPolicy")
            .WithTags("AgentPrism", "Retention")
            .WithSummary("Creates or updates the retention policy for a target.")
            .Accepts<RetentionPolicySaveRequest>("application/json");

        builder.MapDelete("/api/retention/{target}", DeleteAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.PlatformAdmin)
            .WithName("AgentPrismDeleteRetentionPolicy")
            .WithTags("AgentPrism", "Retention")
            .WithSummary("Deletes the retention policy for a target.");
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
