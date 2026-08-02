namespace AgentPrism;

/// <summary>Kodda tanimlanmis bir skill kaydi.</summary>
internal sealed class CodeSkillRegistration
{
    public CodeSkillRegistration(AgentSkillDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
    }

    public AgentSkillDefinition Definition { get; }
}
