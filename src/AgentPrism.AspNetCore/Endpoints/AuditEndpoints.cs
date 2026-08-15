using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace AgentPrism;

/// <summary>Audit trail read endpoints.</summary>
/// <remarks>
/// Read-only: there is no delete or amend endpoint, and there will not be. Retention
/// policy is Phase 25's concern.
/// </remarks>
internal static class AuditEndpoints
{
    /// <summary>Maps the audit trail endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/audit", async Task<Ok<IReadOnlyList<AuditEntry>>> (
                IAuditLog auditLog,
                ITenantContext tenants,
                string? actor,
                string? action,
                string? entity,
                DateTimeOffset? after,
                DateTimeOffset? before,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                var entries = await auditLog.QueryAsync(
                    new AuditQuery
                    {
                        TenantId = tenants.TenantId,
                        Actor = actor,
                        Action = action,
                        Entity = entity,
                        After = after,
                        Before = before,
                        Limit = limit is { } max ? Math.Clamp(max, 1, 500) : 100,
                    },
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(entries);
            })
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AuditRead)
            .WithName("AgentPrismListAudit")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("Lists audit entries, filterable by actor, action, entity, and date range.")
            .WithDescription(
                "Filterable by actor, action, entity, and date range. Runs (an agent processing " +
                "a message) are not written to this log; the runs table already keeps the full " +
                "record. The one exception is the 'content.blocked' action (Phase 48): an " +
                "IContentGuard's block decision is a GOVERNANCE decision, not a run detail, and " +
                "must remain traceable even after the run record is deleted by retention policy. " +
                "The entry carries only the guard and rule name, never the blocked TEXT.");

        builder.MapGet("/api/audit/{entity}", async Task<Ok<IReadOnlyList<AuditEntry>>> (
                string entity,
                IAuditLog auditLog,
                ITenantContext tenants,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                var entries = await auditLog.QueryAsync(
                    new AuditQuery
                    {
                        TenantId = tenants.TenantId,
                        Entity = entity,
                        Limit = limit is { } max ? Math.Clamp(max, 1, 500) : 100,
                    },
                    cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(entries);
            })
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AuditRead)
            .WithName("AgentPrismGetEntityAudit")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("Returns a single entity's change history, newest first.");
    }
}
