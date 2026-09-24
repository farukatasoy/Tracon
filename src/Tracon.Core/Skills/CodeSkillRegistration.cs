namespace Tracon;

/// <summary>A skill registration defined in code.</summary>
internal sealed class CodeSkillRegistration
{
    public CodeSkillRegistration(AgentSkillDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // The origin is stamped here, the one place a code skill is created, so
        // every reader - the catalog, the grant endpoint, GET /api/skills/{name} -
        // sees the same answer to "does this definition come from code".
        Definition = definition with { Origin = AgentDefinitionOrigin.Code };
    }

    public AgentSkillDefinition Definition { get; }
}
