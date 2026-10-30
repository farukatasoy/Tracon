using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Http;

namespace Tracon;

/// <summary>
/// The rules a stored skill has to pass to be written: the ones <c>PUT /api/skills/{name}</c> applies, and the
/// ones an import applies to each skill of a package.
/// </summary>
/// <remarks>
/// One set of rules for both writers (K-455). The script permission a skill's scripts need to RUN is not
/// part of writing the skill, and an import never carries it.
/// </remarks>
internal static class AgentSkillWriteRules
{
    /// <summary>Refuses a write to a name a skill defined in code holds.</summary>
    /// <param name="catalog">The skill catalog.</param>
    /// <param name="name">The skill name.</param>
    /// <returns>The refusal; <see langword="null"/> when no code skill has the name.</returns>
    /// <remarks>
    /// The same rule as a code-defined agent: code wins the name, so a stored
    /// copy would never run, and the console editor - which reads the resolved
    /// skill - would otherwise write the code content over it.
    /// </remarks>
    public static WriteRuleFinding? CodeDefinedName(AgentSkillCatalog catalog, string name)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return catalog.IsDefinedInCode(name)
            ? WriteRuleFinding.Conflict(
                "Code-defined skill cannot be modified",
                $"'{name}' is a skill defined in code. A skill defined in code wins name conflicts, " +
                "so a stored skill with the same name would never run; update the application code " +
                "to change it.",
                WriteRuleCodes.SkillCodeDefined)
            : null;
    }

    /// <summary>Checks the skill's own fields: name, description, instructions and resources.</summary>
    /// <param name="request">The skill to write.</param>
    /// <param name="options">The skill limits.</param>
    /// <returns>The refusal; <see langword="null"/> when the skill is valid.</returns>
    public static WriteRuleFinding? Validate(AgentSkillRequest request, TraconSkillOptions options)
    {
        if (!AgentSkillFrontmatter.ValidateName(request.Name, out var nameReason))
        {
            return InvalidSkill("Skill name invalid", nameReason);
        }

        if (!AgentSkillFrontmatter.ValidateDescription(request.Description, out var descriptionReason))
        {
            return InvalidSkill("Skill description invalid", descriptionReason);
        }

        if (request.Compatibility is { Length: > 0 }
            && !AgentSkillFrontmatter.ValidateCompatibility(request.Compatibility, out var compatibilityReason))
        {
            return InvalidSkill("Skill compatibility invalid", compatibilityReason);
        }

        if (Encoding.UTF8.GetByteCount(request.Instructions) > options.MaxInstructionsLength)
        {
            return InvalidSkill("Skill instructions too large", $"instructions may be at most {options.MaxInstructionsLength} bytes.");
        }

        if (request.Resources.Count > options.MaxResourcesPerSkill)
        {
            return InvalidSkill("Too many resources", $"A skill may carry at most {options.MaxResourcesPerSkill} resources.");
        }

        var resourceNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in request.Resources)
        {
            if (string.IsNullOrWhiteSpace(resource.Name) || !resourceNames.Add(resource.Name))
            {
                return InvalidSkill("Resource name invalid", "Every resource name must be non-empty and unique within the skill.");
            }

            if (Encoding.UTF8.GetByteCount(resource.Content) > options.MaxResourceContentLength)
            {
                return InvalidSkill("Skill resource too large", $"Each resource may be at most {options.MaxResourceContentLength} bytes.");
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
    public static WriteRuleFinding? ValidateScripts(
        AgentSkillRequest request,
        TraconSkillScriptOptions options)
    {
        if (request.Scripts.Count == 0)
        {
            return null;
        }

        if (request.Scripts.Count > options.MaxScriptsPerSkill)
        {
            return InvalidScript(
                "Too many scripts",
                $"A skill may carry at most {options.MaxScriptsPerSkill} scripts.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var script in request.Scripts)
        {
            if (string.IsNullOrWhiteSpace(script.Name) || !names.Add(script.Name))
            {
                return InvalidScript(
                    "Script name invalid",
                    "Every script name must be non-empty and unique within the skill.");
            }

            // The name becomes a file name under the run's scratch directory, so
            // it may not carry a path. Rejected here so the caller sees 400 at
            // save time rather than a failed run later.
            if (!SkillScriptNaming.IsSafeFileName(script.Name))
            {
                return InvalidScript(
                    "Script name invalid",
                    "A script name must be a plain file name: no directory separator, no path root, no '..'.");
            }

            var extension = script.Extension.TrimStart('.');
            if (extension.Length == 0 || !options.Interpreters.ContainsKey(extension))
            {
                return InvalidScript(
                    "Script extension not allowed",
                    $"There is no registered interpreter for the '{script.Extension}' extension.");
            }

            if (Encoding.UTF8.GetByteCount(script.Content) > options.MaxScriptContentLength)
            {
                return InvalidScript(
                    "Script too large",
                    $"Each script may be at most {options.MaxScriptContentLength} bytes.");
            }

            if (script.ParametersSchema is { Length: > 0 } schema && !IsJsonObject(schema))
            {
                return InvalidScript(
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

    private static WriteRuleFinding InvalidSkill(string title, string detail)
        => WriteRuleFinding.BadRequest(title, detail, WriteRuleCodes.SkillInvalid);

    private static WriteRuleFinding InvalidScript(string title, string detail)
        => WriteRuleFinding.BadRequest(title, detail, WriteRuleCodes.SkillScriptInvalid);
}
