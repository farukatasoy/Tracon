using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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
                "A grant for a skill stored in the database or registered in code pins the " +
                "content it is given for: send 'expectedContentHash', read from GET " +
                "/api/skills/{name} (scripts[].contentHash for one script, scriptSetHash when " +
                "'scriptName' is omitted and the grant covers every script). A missing or " +
                "malformed hash returns 400, a 'scriptName' the skill does not carry 404, and a " +
                "hash that no longer matches the content 409 - read the skill again and review " +
                "it; the response never carries the current hash. A script whose content " +
                "changes after the grant does not run until it is granted again. In a " +
                "multi-tenant host a grant for a stored skill also needs platform authority " +
                "(an API key with PlatformAdmin, the static token, or the Tracon.PlatformAdmin " +
                "policy; 403 otherwise), because the script runs under the server's own " +
                "operating-system identity. A name no stored or code skill carries is granted " +
                "without a pin and authorizes scripts read from disk only. A key needs " +
                "AgentsRead to read the hash and SecurityAdmin to grant. Granting while script " +
                "execution is switched off returns 409 rather than succeeding: a grant that " +
                "reads as active but never allows a run would be misleading. 'expiresAt' is " +
                "optional but must be in the future when given (400 otherwise); without it the " +
                "grant does not expire. A refused request writes nothing; every grant is " +
                "written to the audit trail.")
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
        AgentSkillCatalog catalog,
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

        var scriptName = string.IsNullOrWhiteSpace(request.ScriptName) ? null : request.ScriptName;

        // 🚨 Every refusal below returns BEFORE the store is called: no grant row and
        // no script.grant audit entry may exist for a request that was refused.
        var (contentHash, refused) = await PinAsync(httpContext, catalog, request, scriptName, cancellationToken)
            .ConfigureAwait(false);

        if (refused is not null)
        {
            return refused;
        }

        var grant = await store.GrantAsync(
            new SkillScriptGrant
            {
                TenantId = tenantContext.TenantId,
                SkillName = request.SkillName,
                ScriptName = scriptName,
                ContentHash = contentHash,
                GrantedBy = actorResolver.Resolve(),
                GrantedAt = now,
                ExpiresAt = request.ExpiresAt,
            },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Created($"/api/skill-script-grants/{grant.SkillName}", grant);
    }

    /// <summary>
    /// Decides which content the grant pins, or refuses the request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The skill is resolved the way the runtime resolves it: a code skill wins over
    /// a stored one with the same name, so the hash the reviewer read and the hash
    /// compared here describe the content that will run. A disabled stored skill is
    /// found too; otherwise it would be granted without a pin and without the
    /// platform check.
    /// </para>
    /// <para>
    /// A name no source carries yet is granted without a pin: such a grant authorizes
    /// scripts on disk only, and a stored skill created later under the name is
    /// refused under it.
    /// </para>
    /// </remarks>
    private static async ValueTask<(string? ContentHash, ProblemHttpResult? Refused)> PinAsync(
        HttpContext httpContext,
        AgentSkillCatalog catalog,
        SkillScriptGrantRequest request,
        string? scriptName,
        CancellationToken cancellationToken)
    {
        var skill = await catalog.FindWithOriginAsync(request.SkillName, cancellationToken).ConfigureAwait(false);

        if (skill is null)
        {
            return (null, null);
        }

        if (!SkillScriptHashing.IsWellFormed(request.ExpectedContentHash))
        {
            return (null, Invalid(
                "Content hash required",
                $"'{request.SkillName}' has scripts that run from the database or from code, so the grant pins " +
                "their content. Send 'expectedContentHash': 64 hexadecimal characters read from " +
                $"GET /api/skills/{request.SkillName} - scripts[].contentHash for one script, scriptSetHash " +
                "for every script."));
        }

        AgentSkillScriptDefinition? script = null;

        if (scriptName is not null)
        {
            script = skill.Scripts.FirstOrDefault(candidate => string.Equals(candidate.Name, scriptName, StringComparison.Ordinal));

            if (script is null)
            {
                return (null, TypedResults.Problem(
                    title: "Script not found",
                    detail: $"Skill '{request.SkillName}' has no script named '{scriptName}'.",
                    statusCode: StatusCodes.Status404NotFound));
            }
        }

        // A stored script runs under the server's own operating-system identity, so in
        // a multi-tenant host granting one is an installation-level decision, not a
        // tenant's. The tenancy condition is checked HERE: the key branch of the
        // authority check does not look at tenancy, and an unconditional call would
        // refuse every key without PlatformAdmin in a single-tenant host. A null target
        // means "the whole installation"; the caller's own tenant would pass at once.
        if (skill.Origin == AgentDefinitionOrigin.Database
            && httpContext.RequestServices.GetService<IOptions<TraconTenancyOptions>>()?.Value.Enabled == true
            && await CrossTenantAuthority.CheckAsync(httpContext, targetTenantId: null).ConfigureAwait(false) is { } denied)
        {
            return (null, denied);
        }

        var current = script?.ContentHash ?? skill.ScriptSetHash;

        if (!string.Equals(current, request.ExpectedContentHash, StringComparison.OrdinalIgnoreCase))
        {
            // The current hash is deliberately left out: handing it to a blind retry
            // would reopen the window between reading the content and granting it.
            return (null, TypedResults.Problem(
                title: "Content changed",
                detail: $"The content of '{request.SkillName}' no longer matches 'expectedContentHash'. " +
                        "Read the skill again, review what will run, and grant the new hash.",
                statusCode: StatusCodes.Status409Conflict));
        }

        return (current, null);
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
