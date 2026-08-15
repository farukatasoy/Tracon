using System.Diagnostics.CodeAnalysis;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism;

/// <summary>The default implementation of <see cref="IAgentPrismBuilder"/>.</summary>
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

    [RequiresUnreferencedCode("Creating a tool from a method uses reflection; method metadata can be removed from trimmed applications.")]
    [RequiresDynamicCode("Creating a tool from a method can require run-time code generation.")]
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

    [RequiresUnreferencedCode("Tool scanning uses reflection; method metadata can be removed from trimmed applications.")]
    [RequiresDynamicCode("Tool scanning can require run-time code generation.")]
    public IAgentPrismBuilder AddToolsFrom<T>() => AddToolsFrom(typeof(T));

    [RequiresUnreferencedCode("Tool scanning uses reflection; method metadata can be removed from trimmed applications.")]
    [RequiresDynamicCode("Tool scanning can require run-time code generation.")]
    public IAgentPrismBuilder AddToolsFrom(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        foreach (var registration in ToolMethodScanner.Scan(type))
        {
            Services.AddSingleton(registration);
        }

        return this;
    }

    public IAgentPrismBuilder AddAgent(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        Services.AddSingleton(CodeAgentRegistration.FromDefinition(definition));
        return this;
    }

    public IAgentPrismBuilder AddSkill(AgentSkillDefinition skill)
    {
        ArgumentNullException.ThrowIfNull(skill);

        Services.AddSingleton(new CodeSkillRegistration(skill));
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

    public IAgentPrismBuilder AddEvalCheck(string kind, EvalCheck check)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(check);

        Services.AddSingleton(new AgentPrismEvalCheckRegistration(kind, check));
        return this;
    }
}
