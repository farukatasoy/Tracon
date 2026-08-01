using System.Diagnostics.CodeAnalysis;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism;

/// <summary><see cref="IAgentPrismBuilder"/> arayuzunun varsayilan uygulamasi.</summary>
internal sealed class AgentPrismBuilder : IAgentPrismBuilder
{
    public AgentPrismBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    public IServiceCollection Services { get; }

    public IAgentPrismBuilder Configure(Action<AgentPrismOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        Services.Configure(configure);
        return this;
    }

    public IAgentPrismBuilder AddTool(AIFunction tool, bool requiresApproval = false)
    {
        ArgumentNullException.ThrowIfNull(tool);

        Services.AddSingleton(new AgentPrismToolRegistration(tool, requiresApproval));
        return this;
    }

    [RequiresUnreferencedCode("Metottan tool uretmek yansima kullanir; kirpilmis uygulamalarda tip bilgisi kaybolabilir.")]
    [RequiresDynamicCode("Metottan tool uretmek calisma aninda kod uretimi gerektirebilir.")]
    public IAgentPrismBuilder AddTool(
        Delegate method,
        string? name = null,
        string? description = null,
        bool requiresApproval = false)
    {
        ArgumentNullException.ThrowIfNull(method);

        var tool = AIFunctionFactory.Create(method, name, description);
        return AddTool(tool, requiresApproval);
    }

    public IAgentPrismBuilder AddAgent(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        Services.AddSingleton(CodeAgentRegistration.FromDefinition(definition));
        return this;
    }

    public IAgentPrismBuilder AddAgent(string name, Func<IServiceProvider, AIAgent> factory, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        Services.AddSingleton(CodeAgentRegistration.FromFactory(name, factory, description));
        return this;
    }

    public IAgentPrismBuilder AddModelProvider(IModelProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        Services.AddSingleton(provider);
        return this;
    }

    public IAgentPrismBuilder AddModelProvider(Func<IServiceProvider, IModelProvider> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        Services.AddSingleton(factory);
        return this;
    }
}
