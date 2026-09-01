using System.Diagnostics.CodeAnalysis;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    public IAgentPrismBuilder AddTool(AIFunction tool, Action<ToolRegistrationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ToolRegistrationOptions();
        configure(options);
        Services.AddSingleton(new AgentPrismToolRegistration(
            tool,
            options.RequiresApproval,
            options.Source,
            options.Effect,
            options.RequiredPermission,
            options.Timeout,
            options.SafeToRepeat,
            options.MaxOutputBytes));
        return this;
    }

    public IAgentPrismBuilder AddTool(AIFunction tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return AddTool(tool, static _ => { });
    }

    public IAgentPrismBuilder AddScopedTool(AIFunction tool, Action<ToolRegistrationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ToolRegistrationOptions();
        configure(options);

        // The scope factory can only be resolved from the FINAL provider, so
        // the registration itself is built lazily through a factory, unlike
        // AddTool's eager instance registration.
        Services.AddSingleton<AgentPrismToolRegistration>(provider => new AgentPrismToolRegistration(
            new ScopedAIFunction(tool, provider.GetRequiredService<IServiceScopeFactory>()),
            options.RequiresApproval,
            options.Source,
            options.Effect,
            options.RequiredPermission,
            options.Timeout,
            options.SafeToRepeat,
            options.MaxOutputBytes));
        return this;
    }

    public IAgentPrismBuilder AddScopedTool(AIFunction tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return AddScopedTool(tool, static _ => { });
    }

    [RequiresUnreferencedCode("Creating a tool from a method uses reflection; method metadata can be removed from trimmed applications.")]
    [RequiresDynamicCode("Creating a tool from a method can require run-time code generation.")]
    public IAgentPrismBuilder AddTool(
        Delegate method,
        string? name = null,
        string? description = null,
        Action<ToolRegistrationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        return configure is null
            ? AddTool(AIFunctionFactory.Create(method, name, description))
            : AddTool(AIFunctionFactory.Create(method, name, description), configure);
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

    public IAgentPrismBuilder AddAgentSource<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSource>()
        where TSource : class, IAgentSource
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentSource, TSource>());
        return this;
    }

    public IAgentPrismBuilder AddAgentSource(IAgentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Services.AddSingleton(source);
        return this;
    }

    public IAgentPrismBuilder AddAgentSource(Func<IServiceProvider, IAgentSource> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        Services.AddSingleton(factory);
        return this;
    }

    public IAgentPrismBuilder AddAgentDecorator<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDecorator>()
        where TDecorator : class, IAgentDecorator
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentDecorator, TDecorator>());
        return this;
    }

    public IAgentPrismBuilder AddAgentDecorator(IAgentDecorator decorator)
    {
        ArgumentNullException.ThrowIfNull(decorator);

        Services.AddSingleton(decorator);
        return this;
    }

    public IAgentPrismBuilder AddAgentDecorator(Func<IServiceProvider, IAgentDecorator> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        Services.AddSingleton(factory);
        return this;
    }

    public IAgentPrismBuilder AddRunJudge<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TJudge>()
        where TJudge : class, IRunJudge
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IRunJudge, TJudge>());
        return this;
    }

    public IAgentPrismBuilder AddRunJudge(IRunJudge judge)
    {
        ArgumentNullException.ThrowIfNull(judge);

        Services.AddSingleton(judge);
        return this;
    }

    public IAgentPrismBuilder AddRunJudge(Func<IServiceProvider, IRunJudge> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        Services.AddSingleton(factory);
        return this;
    }

    public IAgentPrismBuilder AddModelProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>()
        where TProvider : class, IModelProvider
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelProvider, TProvider>());
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
