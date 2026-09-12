using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Tracon;

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
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapGet("/api/skill-script-grants", ListAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("TraconListSkillScriptGrants")
            .WithTags("Tracon", "Governance")
            .WithSummary("Lists the tenant's script run grants.")
            .WithDescription(
                "A grant is permission to execute code on the server, so this list is the " +
                "authoritative answer to 'what may run here'. A grant with no script name covers " +
                "every script in that skill; one with a script name covers only that script. " +
                "Entries may carry an expiry, and an expired grant no longer authorizes a run. " +
                "Grants are also a retention target, so old ones are cleaned up.");

        builder.MapPost("/api/skill-script-grants", GrantAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("TraconGrantSkillScript")
            .WithTags("Tracon", "Governance")
            .WithSummary("Grants run permission to a skill script.")
            .WithDescription(
                "Granting while script execution is switched off returns 409 rather than " +
                "succeeding: a grant that reads as active but never allows a run would be " +
                "misleading. Omit 'scriptName' to cover every script in the skill. 'expiresAt' " +
                "is optional but must be in the future when given (400 otherwise); without it " +
                "the grant does not expire. Every change is written to the audit trail.")
            .Accepts<SkillScriptGrantRequest>("application/json");

        builder.MapDelete("/api/skill-script-grants/{skillName}", RevokeAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("TraconRevokeSkillScript")
            .WithTags("Tracon", "Governance")
            .WithSummary("Revokes a script run grant.")
            .WithDescription(
                "Revoking takes effect on the next run; a script already executing is not " +
                "stopped. The optional '?scriptName=' must match how the grant was created — " +
                "revoking one script does not remove a skill-wide grant, and the skill-wide " +
                "grant keeps authorizing that script until it too is revoked. When no matching " +
                "active grant exists the response is 404.");
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
        IOptions<TraconOptions> options,
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
