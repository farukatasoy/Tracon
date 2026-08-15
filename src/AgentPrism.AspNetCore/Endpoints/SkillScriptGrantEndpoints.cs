using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Management endpoints for skill script run grants.</summary>
/// <remarks>
/// Granting is granting authority to run code on the server. That is why every endpoint
/// other than read requires the admin role, and the audit trail decorator in the store
/// layer records every change.
/// </remarks>
internal static class SkillScriptGrantEndpoints
{
    /// <summary>Maps the grant endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/skill-script-grants", ListAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("AgentPrismListSkillScriptGrants")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("Lists the tenant's script run grants.");

        builder.MapPost("/api/skill-script-grants", GrantAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("AgentPrismGrantSkillScript")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("Grants run permission to a skill script.")
            .Accepts<SkillScriptGrantRequest>("application/json");

        builder.MapDelete("/api/skill-script-grants/{skillName}", RevokeAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("AgentPrismRevokeSkillScript")
            .WithTags("AgentPrism", "Governance")
            .WithSummary("Revokes a script run grant.");
    }

    private static async Task<Ok<IReadOnlyList<SkillScriptGrant>>> ListAsync(
        ISkillScriptGrantStore store,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
        => TypedResults.Ok(await store.ListAsync(tenantContext.TenantId, cancellationToken).ConfigureAwait(false));

    private static async Task<Results<Created<SkillScriptGrant>, ProblemHttpResult>> GrantAsync(
        HttpContext httpContext,
        ISkillScriptGrantStore store,
        ITenantContext tenantContext,
        IAuditActorResolver actorResolver,
        IOptions<AgentPrismOptions> options,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<SkillScriptGrantRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        // Granting access while script running is disabled would be misleading: the UI
        // would show "granted", but the run would still be rejected.
        if (!options.Value.Skills.Scripts.Enabled)
        {
            return TypedResults.Problem(
                title: "Script running disabled",
                detail: "Enable script running with UseSkillScripts(...) before granting access.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (string.IsNullOrWhiteSpace(request.SkillName))
        {
            return Invalid("Skill name required", "skillName cannot be empty.");
        }

        var now = DateTimeOffset.UtcNow;
        if (request.ExpiresAt is { } expires && expires <= now)
        {
            return Invalid("Expiration in the past", "expiresAt must be a moment in the future.");
        }

        var grant = await store.GrantAsync(
            new SkillScriptGrant
            {
                TenantId = tenantContext.TenantId,
                SkillName = request.SkillName,
                ScriptName = string.IsNullOrWhiteSpace(request.ScriptName) ? null : request.ScriptName,
                GrantedBy = actorResolver.Resolve(),
                GrantedAt = now,
                ExpiresAt = request.ExpiresAt,
            },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Created($"/api/skill-script-grants/{grant.SkillName}", grant);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeAsync(
        [FromRoute] string skillName,
        [FromQuery] string? scriptName,
        ISkillScriptGrantStore store,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
        => await store.RevokeAsync(tenantContext.TenantId, skillName, scriptName, cancellationToken).ConfigureAwait(false)
            ? TypedResults.NoContent()
            : TypedResults.Problem(
                title: "Grant not found",
                detail: $"There is no active run grant for '{skillName}'.",
                statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult Invalid(string title, string detail)
        => TypedResults.Problem(title: title, detail: detail, statusCode: StatusCodes.Status400BadRequest);
}
