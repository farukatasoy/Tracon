using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Skill script calistirma izinlerinin yonetim uclari.</summary>
/// <remarks>
/// Izin vermek, sunucuda kod calistirma yetkisi vermektir. Bu yuzden okuma
/// disindaki her uc yonetici rolune baglidir ve depo katmanindaki denetim izi
/// dekoratoru her degisikligi kaydeder.
/// </remarks>
internal static class SkillScriptGrantEndpoints
{
    /// <summary>Izin uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/skill-script-grants", ListAsync)
            .RequireRole(roles.Reader)
            .WithName("AgentPrismListSkillScriptGrants")
            .WithSummary("Kiracinin script calistirma izinlerini listeler.");

        builder.MapPost("/api/skill-script-grants", GrantAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismGrantSkillScript")
            .WithSummary("Bir skill script'ine calistirma izni verir.");

        builder.MapDelete("/api/skill-script-grants/{skillName}", RevokeAsync)
            .RequireRole(roles.Admin)
            .WithName("AgentPrismRevokeSkillScript")
            .WithSummary("Bir script calistirma iznini iptal eder.");
    }

    private static async Task<Ok<IReadOnlyList<SkillScriptGrant>>> ListAsync(
        ISkillScriptGrantStore store,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
        => TypedResults.Ok(await store.ListAsync(tenantContext.TenantId, cancellationToken).ConfigureAwait(false));

    private static async Task<Results<Created<SkillScriptGrant>, ProblemHttpResult>> GrantAsync(
        [FromBody] SkillScriptGrantRequest request,
        ISkillScriptGrantStore store,
        ITenantContext tenantContext,
        IAuditActorResolver actorResolver,
        IOptions<AgentPrismOptions> options,
        CancellationToken cancellationToken)
    {
        // Script calistirma kapali iken izin vermek yaniltici olurdu: arayuz
        // "izin verildi" gosterir, calistirma yine reddedilirdi.
        if (!options.Value.Skills.Scripts.Enabled)
        {
            return TypedResults.Problem(
                title: "Script calistirma kapali",
                detail: "Izin vermeden once UseSkillScripts(...) ile script calistirmayi acin.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (string.IsNullOrWhiteSpace(request.SkillName))
        {
            return Invalid("Skill adi gerekli", "skillName bos olamaz.");
        }

        var now = DateTimeOffset.UtcNow;
        if (request.ExpiresAt is { } expires && expires <= now)
        {
            return Invalid("Bitis zamani gecmiste", "expiresAt gelecekte bir an olmalidir.");
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
                title: "Izin bulunamadi",
                detail: $"'{skillName}' icin gecerli bir calistirma izni yok.",
                statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult Invalid(string title, string detail)
        => TypedResults.Problem(title: title, detail: detail, statusCode: StatusCodes.Status400BadRequest);
}
