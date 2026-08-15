using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Markdown-based skill management endpoints.</summary>
internal static class SkillEndpoints
{
    /// <summary>Maps the skill endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapGet("/api/skills", ListAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("AgentPrismListSkills")
            .WithTags("AgentPrism", "Skills")
            .WithSummary("Lists the tenant's skills.");

        builder.MapGet("/api/skills/{name}", GetAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("AgentPrismGetSkill")
            .WithTags("AgentPrism", "Skills")
            .WithSummary("Returns a single skill and its resources.");

        builder.MapPut("/api/skills/{name}", SaveAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .WithName("AgentPrismSaveSkill")
            .WithTags("AgentPrism", "Skills")
            .WithSummary("Creates or updates a skill.")
            .Accepts<AgentSkillRequest>("application/json");

        builder.MapDelete("/api/skills/{name}", DeleteAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .WithName("AgentPrismDeleteSkill")
            .WithTags("AgentPrism", "Skills")
            .WithSummary("Deletes a skill and its cascading resources.");
    }

    private static async Task<Ok<IReadOnlyList<AgentSkillDefinition>>> ListAsync(
        IAgentSkillStore store,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
        => TypedResults.Ok(await store.ListAsync(tenantContext.TenantId, cancellationToken).ConfigureAwait(false));

    private static async Task<Results<Ok<AgentSkillDefinition>, ProblemHttpResult>> GetAsync(
        string name,
        IAgentSkillStore store,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        var skill = await store.GetAsync(tenantContext.TenantId, name, cancellationToken).ConfigureAwait(false);
        return skill is { } ? TypedResults.Ok(skill) : NotFound(name);
    }

    private static async Task<Results<Ok<AgentSkillDefinition>, Created<AgentSkillDefinition>, ProblemHttpResult>> SaveAsync(
        string name,
        IAgentSkillStore store,
        ITenantContext tenantContext,
        IOptions<AgentPrismOptions> options,
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

        if (Validate(request, options.Value.Skills) is { } invalid)
        {
            return invalid;
        }

        if (ValidateScripts(request, options.Value.Skills.Scripts) is { } invalidScript)
        {
            return invalidScript;
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
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
        => await store.DeleteAsync(tenantContext.TenantId, name, cancellationToken).ConfigureAwait(false)
            ? TypedResults.NoContent()
            : NotFound(name);

    private static ProblemHttpResult? Validate(AgentSkillRequest request, AgentPrismSkillOptions options)
    {
        if (!AgentSkillFrontmatter.ValidateName(request.Name, out var nameReason))
        {
            return Invalid("Skill name invalid", nameReason);
        }

        if (!AgentSkillFrontmatter.ValidateDescription(request.Description, out var descriptionReason))
        {
            return Invalid("Skill description invalid", descriptionReason);
        }

        if (request.Compatibility is { Length: > 0 }
            && !AgentSkillFrontmatter.ValidateCompatibility(request.Compatibility, out var compatibilityReason))
        {
            return Invalid("Skill compatibility invalid", compatibilityReason);
        }

        if (Encoding.UTF8.GetByteCount(request.Instructions) > options.MaxInstructionsLength)
        {
            return Invalid("Skill instructions too large", $"instructions may be at most {options.MaxInstructionsLength} bytes.");
        }

        if (request.Resources.Count > options.MaxResourcesPerSkill)
        {
            return Invalid("Too many resources", $"A skill may carry at most {options.MaxResourcesPerSkill} resources.");
        }

        var resourceNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in request.Resources)
        {
            if (string.IsNullOrWhiteSpace(resource.Name) || !resourceNames.Add(resource.Name))
            {
                return Invalid("Resource name invalid", "Every resource name must be non-empty and unique within the skill.");
            }

            if (Encoding.UTF8.GetByteCount(resource.Content) > options.MaxResourceContentLength)
            {
                return Invalid("Skill resource too large", $"Each resource may be at most {options.MaxResourceContentLength} bytes.");
            }
        }

        return null;
    }

    /// <summary>
    /// Validates the script definitions.
    /// </summary>
    /// <remarks>
    /// Scripts can be saved <strong>even when execution is disabled</strong>; saving and
    /// executing are separate permissions. However, an extension that is not on the
    /// interpreter allowlist is rejected: such a saved script could never be run
    /// and would silently leave dead data behind.
    /// </remarks>
    private static ProblemHttpResult? ValidateScripts(
        AgentSkillRequest request,
        AgentPrismSkillScriptOptions options)
    {
        if (request.Scripts.Count == 0)
        {
            return null;
        }

        if (request.Scripts.Count > options.MaxScriptsPerSkill)
        {
            return Invalid(
                "Too many scripts",
                $"A skill may carry at most {options.MaxScriptsPerSkill} scripts.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var script in request.Scripts)
        {
            if (string.IsNullOrWhiteSpace(script.Name) || !names.Add(script.Name))
            {
                return Invalid(
                    "Script name invalid",
                    "Every script name must be non-empty and unique within the skill.");
            }

            var extension = script.Extension.TrimStart('.');
            if (extension.Length == 0 || !options.Interpreters.ContainsKey(extension))
            {
                return Invalid(
                    "Script extension not allowed",
                    $"There is no registered interpreter for the '{script.Extension}' extension.");
            }

            if (Encoding.UTF8.GetByteCount(script.Content) > options.MaxScriptContentLength)
            {
                return Invalid(
                    "Script too large",
                    $"Each script may be at most {options.MaxScriptContentLength} bytes.");
            }

            if (script.ParametersSchema is { Length: > 0 } schema && !IsJsonObject(schema))
            {
                return Invalid(
                    "Parameter schema invalid",
                    "parametersSchema must be a valid JSON object.");
            }
        }

        return null;
    }

    private static bool IsJsonObject(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ProblemHttpResult Invalid(string title, string detail)
        => TypedResults.Problem(title: title, detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult NotFound(string name)
        => TypedResults.Problem(
            title: "Skill not found",
            detail: $"There is no skill named '{name}'.",
            statusCode: StatusCodes.Status404NotFound);
}
