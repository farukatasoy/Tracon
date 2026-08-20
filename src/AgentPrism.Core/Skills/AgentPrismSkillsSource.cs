using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>Converts AgentPrism skill definitions to an MAF skill source.</summary>
internal sealed class AgentPrismSkillsSource : AgentSkillsSource
{
    /// <summary>
    /// MAF calls <c>MakeReadOnly()</c> on this instance in
    /// <c>AgentInlineSkill</c>/<c>AddScript</c>. Without a source-generated
    /// <c>TypeInfoResolver</c>, this throws an exception that a resolver is required
    /// before it can be read-only. Script arguments always flow through
    /// MarshalArguments as <see cref="JsonElement"/>, which the source generator resolves without reflection.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = AgentPrismSkillsJsonContext.Default,
    };

    private readonly AgentSkillCatalog _catalog;
    private readonly IReadOnlyList<string> _skillNames;
    private readonly SkillScriptSupport? _scripts;

    public AgentPrismSkillsSource(
        AgentSkillCatalog catalog,
        AgentDefinition definition,
        SkillScriptSupport? scripts = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(definition);

        _catalog = catalog;
        _skillNames = definition.SkillNames;
        _scripts = scripts;
    }

    public override async Task<IList<AgentSkill>> GetSkillsAsync(
        AgentSkillsSourceContext context,
        CancellationToken cancellationToken = default)
    {
        var definitions = await _catalog.GetEnabledAsync(_skillNames, cancellationToken).ConfigureAwait(false);
        var skills = new List<AgentSkill>(definitions.Count);

        foreach (var definition in definitions)
        {
            skills.Add(CreateSkill(definition));
        }

        return skills;
    }

    private AgentInlineSkill CreateSkill(AgentSkillDefinition definition)
    {
        var frontmatter = new AgentSkillFrontmatter(definition.Name, definition.Description, definition.Compatibility)
        {
            License = definition.License,
            AllowedTools = definition.AllowedTools,
            Metadata = new AdditionalPropertiesDictionary(
                definition.Metadata.Select(static pair => new KeyValuePair<string, object?>(pair.Key, pair.Value))),
        };
        var skill = new AgentInlineSkill(
            frontmatter,
            definition.Instructions,
            SerializerOptions,
            MarshalArguments);

        foreach (var resource in definition.Resources)
        {
            // MAF does not provide a comparer in this overload. Names are unique at
            // the HTTP and store boundary, so no additional comparison is needed here.
#pragma warning disable MA0002
            skill.AddResource(resource.Name, resource.Content, resource.Description ?? string.Empty);
#pragma warning restore MA0002
        }

        // Stored scripts are visible to the model only when the feature and
        // AllowStoredScripts are enabled. When disabled, the record stays in the
        // database but never runs, and the model cannot call a script it cannot see.
        if (_scripts is { StoredScriptsEnabled: true })
        {
            foreach (var script in definition.Scripts)
            {
#pragma warning disable MA0002
                skill.AddScript(
                    script.Name,
                    _scripts.CreateStoredScriptDelegate(definition.Name, script),
                    DescribeScript(script),
                    SerializerOptions);
#pragma warning restore MA0002
            }
        }

        return skill;
    }

    /// <summary>
    /// Marshals model-generated JSON to the only parameter of a stored script delegate.
    /// </summary>
    /// <remarks>
    /// Stored script argument schema arrives as text at run time and cannot become a
    /// delegate signature. Raw JSON therefore passes as one <c>arguments</c> parameter,
    /// while the script description informs the model about the schema.
    /// </remarks>
    private static AIFunctionArguments MarshalArguments(JsonElement? arguments)
    {
        var marshaled = new AIFunctionArguments(StringComparer.Ordinal);

        if (arguments is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } element)
        {
            marshaled["arguments"] = element.GetRawText();
        }

        return marshaled;
    }

    private static string DescribeScript(AgentSkillScriptDefinition script)
       => script.ParametersSchema is { Length: > 0 } schema
           ? $"{script.Description} Arguments are supplied as one JSON object. Schema: {schema}"
           : $"{script.Description} This script does not take arguments.";
}
