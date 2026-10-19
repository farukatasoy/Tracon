namespace Tracon;

/// <summary>An <see cref="IAgentExtension"/> built from a delegate by <c>AddAgentExtension(name, configure)</c>.</summary>
internal sealed class DelegateAgentExtension : IAgentExtension
{
    private readonly Action<AgentExtensionContext> _configure;

    public DelegateAgentExtension(string name, Action<AgentExtensionContext> configure, string? description)
    {
        Name = name;
        Description = description;
        _configure = configure;
    }

    public string Name { get; }

    public string? Description { get; }

    public void Configure(AgentExtensionContext context) => _configure(context);
}
