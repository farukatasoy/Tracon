using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Markdown-based skill management endpoints.</summary>
internal static class SkillEndpoints
{
    /// <summary>Maps the skill endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapGet("/api/skills", ListAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("TraconListSkills")
            .WithTags("Tracon", "Skills")
            .WithSummary("Lists the tenant's skills.")
            .WithDescription(
                "Skills are scoped to the calling tenant; a skill defined for another tenant is " +
                "never returned. Each entry is complete — instructions, resources, and scripts " +
                "come with it, so a client does not need a second call per skill. The response " +
                "is not paged.");

        builder.MapGet("/api/skills/{name}", GetAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("TraconGetSkill")
            .WithTags("Tracon", "Skills")
            .WithSummary("Returns a single skill and its resources.")
            .WithDescription(
                "The response carries the skill's instructions together with every resource and " +
                "script attached to it, including their content. The name resolves the way the " +
                "runtime resolves it: a skill registered in code wins over a stored skill with the " +
                "same name, and 'origin' says which one was returned (Code or Database) - so the " +
                "content shown is the content that runs. Each script carries 'contentHash' and the " +
                "skill carries 'scriptSetHash'; a script grant pins one of them. A disabled skill " +
                "is returned too. Names are compared exactly, case included; an unknown name " +
                "returns 404. The list (GET /api/skills) shows stored skills only.");

        builder.MapPut("/api/skills/{name}", SaveAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .WithName("TraconSaveSkill")
            .WithTags("Tracon", "Skills")
            .WithSummary("Creates or updates a skill.")
            .WithDescription(
                "The call replaces the whole skill: resources and scripts that the body omits are " +
                "removed. A first save answers 201, a later one 200. The path name and the body " +
                "name must be identical (400 otherwise). Name, description, and compatibility " +
                "follow the skill frontmatter rules, and instructions, resources, and scripts are " +
                "each bounded by the configured size limits. A script may be SAVED even when " +
                "script execution is turned off — saving and running are separate permissions — " +
                "but an extension with no registered interpreter is rejected, because such a " +
                "script could never run and would leave dead data behind. A name registered in " +
                "code answers 409: a skill defined in code wins name conflicts, so a stored skill " +
                "with that name would never run. Changing a stored script's content stops it from " +
                "running until its script grant is given again for the new content.")
            .Accepts<AgentSkillRequest>("application/json");

        builder.MapDelete("/api/skills/{name}", DeleteAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .WithName("TraconDeleteSkill")
            .WithTags("Tracon", "Skills")
            .WithSummary("Deletes a skill and its cascading resources.")
            .WithDescription(
                "Resources and scripts are removed with the skill. Agent definitions that still " +
                "name the skill are NOT rewritten, and they stop resolving: compiling such an " +
                "agent fails with 'the skill was not found' until the reference is removed or the " +
                "skill is recreated. Check the agents that use a skill before deleting it. An " +
                "unknown name returns 404. For a name registered in code only a stored skill with " +
                "that name is removed (one saved before the name was taken in code, which never " +
                "ran); the code skill keeps running, and a name with no stored skill answers 409.");
    }

    private static async Task<Ok<IReadOnlyList<AgentSkillDefinition>>> ListAsync(
        IAgentSkillStore store,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
        => TypedResults.Ok(await store.ListAsync(tenantContext.TenantId, cancellationToken).ConfigureAwait(false));

    private static async Task<Results<Ok<AgentSkillDefinition>, ProblemHttpResult>> GetAsync(
        string name,
        AgentSkillCatalog catalog,
        CancellationToken cancellationToken)
    {
        // Resolved through the catalog, not the store: a reviewer who reads a hash
        // here to grant a script must read the content that will run, and a code
        // skill shadows a stored one with the same name.
        var skill = await catalog.FindWithOriginAsync(name, cancellationToken).ConfigureAwait(false);
        return skill is { } ? TypedResults.Ok(skill) : NotFound(name);
    }

    private static async Task<Results<Ok<AgentSkillDefinition>, Created<AgentSkillDefinition>, ProblemHttpResult>> SaveAsync(
        string name,
        IAgentSkillStore store,
        AgentSkillCatalog catalog,
        ITenantContext tenantContext,
        IOptions<TraconOptions> options,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<AgentSkillRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        if (!string.Equals(name, request.Name, StringComparison.Ordinal))
        {
            return TypedResults.Problem(
                title: "Name mismatch",
                detail: $"The path name is '{name}', the body name is '{request.Name}'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The same rule as a code-defined agent: code wins the name, so a stored
        // copy would never run, and the console editor - which reads the resolved
        // skill - would otherwise write the code content over it.
        if (AgentSkillWriteRules.CodeDefinedName(catalog, name) is { } codeDefined)
        {
            return codeDefined.ToProblem();
        }

        if (AgentSkillWriteRules.Validate(request, options.Value.Skills) is { } invalid)
        {
            return invalid.ToProblem();
        }

        if (AgentSkillWriteRules.ValidateScripts(request, options.Value.Skills.Scripts) is { } invalidScript)
        {
            return invalidScript.ToProblem();
        }

        var exists = await store.GetAsync(tenantContext.TenantId, name, cancellationToken).ConfigureAwait(false) is not null;
        var saved = await store.SaveAsync(request.ToDefinition(tenantContext.TenantId), cancellationToken).ConfigureAwait(false);
        return exists
            ? TypedResults.Ok(saved)
            : TypedResults.Created($"{httpContext.Request.Path}", saved);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        string name,
        IAgentSkillStore store,
        AgentSkillCatalog catalog,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        if (await store.DeleteAsync(tenantContext.TenantId, name, cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.NoContent();
        }

        return catalog.IsDefinedInCode(name)
            ? TypedResults.Problem(
                title: "Code-defined skill cannot be modified",
                detail: $"'{name}' is a skill defined in code and has no stored copy to delete; remove it " +
                        "from the application code.",
                statusCode: StatusCodes.Status409Conflict)
            : NotFound(name);
    }

    private static ProblemHttpResult NotFound(string name)
        => TypedResults.Problem(
            title: "Skill not found",
            detail: $"There is no skill named '{name}'.",
            statusCode: StatusCodes.Status404NotFound);
}
