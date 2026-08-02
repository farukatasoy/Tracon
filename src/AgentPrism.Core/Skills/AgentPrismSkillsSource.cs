using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>AgentPrism skill tanimlarini MAF skill kaynagina cevirir.</summary>
internal sealed class AgentPrismSkillsSource : AgentSkillsSource
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

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
            // MAF bu asiri yuklemede comparer sunmaz. Adlar HTTP ve store
            // sinirinda benzersiz oldugu icin burada ek bir karsilastirma yoktur.
#pragma warning disable MA0002
            skill.AddResource(resource.Name, resource.Content, resource.Description ?? string.Empty);
#pragma warning restore MA0002
        }

        // Saklanan script'ler modele YALNIZCA ozellik ve AllowStoredScripts acikken
        // gorunur. Kapaliyken kayit veritabaninda durur ama hicbir zaman
        // calistirilamaz; gormedigi bir script'i modelin cagirmasi da mumkun degildir.
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
    /// Modelin urettigi JSON'u, saklanan script delegesinin tek parametresine tasir.
    /// </summary>
    /// <remarks>
    /// Saklanan script'lerin arguman semasi calisma aninda, metin olarak gelir ve
    /// bir delege imzasina cevrilemez. Bu yuzden ham JSON tek bir
    /// <c>arguments</c> parametresi olarak gecer; sema, script'in aciklamasinda
    /// modele bildirilir.
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
           ? $"{script.Description} Argumanlar tek bir JSON nesnesi olarak verilir. Sema: {schema}"
           : $"{script.Description} Bu script arguman almaz.";
}
