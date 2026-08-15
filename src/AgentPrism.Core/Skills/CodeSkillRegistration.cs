namespace AgentPrism;

/// <summary>A skill registration defined in code.</summary>
internal sealed class CodeSkillRegistration
{
    public CodeSkillRegistration(AgentSkillDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
    }

    public AgentSkillDefinition Definition { get; }
}
