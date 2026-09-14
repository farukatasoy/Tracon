using System.Diagnostics.CodeAnalysis;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tracon;

/// <summary>The default implementation of <see cref="ITraconBuilder"/>.</summary>
internal sealed class TraconBuilder : ITraconBuilder
{
    public TraconBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    public IServiceCollection Services { get; }

    public ITraconBuilder Configure(Action<TraconOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        Services.Configure(configure);
        return this;
    }

    public ITraconBuilder AddTool(AIFunction tool, Action<ToolRegistrationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ToolRegistrationOptions();
        configure(options);
        Services.AddSingleton(new TraconToolRegistration(
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

    public ITraconBuilder AddTool(AIFunction tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return AddTool(tool, static _ => { });
    }

    public ITraconBuilder AddScopedTool(AIFunction tool, Action<ToolRegistrationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ToolRegistrationOptions();
        configure(options);

        // The scope factory can only be resolved from the FINAL provider, so
        // the registration itself is built lazily through a factory, unlike
        // AddTool's eager instance registration.
        Services.AddSingleton<TraconToolRegistration>(provider => new TraconToolRegistration(
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

    public ITraconBuilder AddScopedTool(AIFunction tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return AddScopedTool(tool, static _ => { });
    }

    [RequiresUnreferencedCode("Creating a tool from a method uses reflection; method metadata can be removed from trimmed applications.")]
    [RequiresDynamicCode("Creating a tool from a method can require run-time code generation.")]
    public ITraconBuilder AddTool(
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
    public ITraconBuilder AddToolsFrom<T>() => AddToolsFrom(typeof(T));

    [RequiresUnreferencedCode("Tool scanning uses reflection; method metadata can be removed from trimmed applications.")]
    [RequiresDynamicCode("Tool scanning can require run-time code generation.")]
    public ITraconBuilder AddToolsFrom(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        foreach (var registration in ToolMethodScanner.Scan(type))
        {
            Services.AddSingleton(registration);
        }

        return this;
    }

    public ITraconBuilder AddAgent(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        Services.AddSingleton(CodeAgentRegistration.FromDefinition(definition));
        return this;
    }

    public ITraconBuilder AddSkill(AgentSkillDefinition skill)
    {
        ArgumentNullException.ThrowIfNull(skill);

        Services.AddSingleton(new CodeSkillRegistration(skill));
        return this;
    }

    public ITraconBuilder AddAgent(string name, Func<IServiceProvider, AIAgent> factory, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        Services.AddSingleton(CodeAgentRegistration.FromFactory(name, factory, description));
        return this;
    }

    public ITraconBuilder AddAgentSource<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSource>()
        where TSource : class, IAgentSource
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentSource, TSource>());
        return this;
    }

    public ITraconBuilder AddAgentSource(IAgentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Services.AddSingleton(source);
        return this;
    }

    public ITraconBuilder AddAgentSource(Func<IServiceProvider, IAgentSource> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        Services.AddSingleton(factory);
        return this;
    }

    public ITraconBuilder AddAgentDecorator<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDecorator>()
        where TDecorator : class, IAgentDecorator
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentDecorator, TDecorator>());
        return this;
    }

    public ITraconBuilder AddAgentDecorator(IAgentDecorator decorator)
    {
        ArgumentNullException.ThrowIfNull(decorator);

        Services.AddSingleton(decorator);
        return this;
    }

    public ITraconBuilder AddAgentDecorator(Func<IServiceProvider, IAgentDecorator> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        Services.AddSingleton(factory);
        return this;
    }

    public ITraconBuilder AddRunJudge<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TJudge>()
        where TJudge : class, IRunJudge
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IRunJudge, TJudge>());
        return this;
    }

    public ITraconBuilder AddRunJudge(IRunJudge judge)
    {
        ArgumentNullException.ThrowIfNull(judge);

        Services.AddSingleton(judge);
        return this;
    }

    public ITraconBuilder AddRunJudge(Func<IServiceProvider, IRunJudge> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        Services.AddSingleton(factory);
        return this;
    }

    public ITraconBuilder AddModelProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>()
        where TProvider : class, IModelProvider
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelProvider, TProvider>());
        return this;
    }

    public ITraconBuilder AddModelProvider(IModelProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        Services.AddSingleton(provider);
        return this;
    }

    public ITraconBuilder AddModelProvider(Func<IServiceProvider, IModelProvider> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        Services.AddSingleton(factory);
        return this;
    }

    public ITraconBuilder AddEvalCheck(string kind, EvalCheck check)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(check);

        Services.AddSingleton(new TraconEvalCheckRegistration(kind, check));
        return this;
    }

    // MAAI001: see the rationale on ITraconBuilder.AddLoopEvaluator.
#pragma warning disable MAAI001
    public ITraconBuilder AddLoopEvaluator(string kind, LoopEvaluator evaluator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(evaluator);

        Services.AddSingleton(new TraconLoopEvaluatorRegistration(kind, evaluator));
        return this;
    }
#pragma warning restore MAAI001

    public ITraconBuilder RequireCustomBinding<T>()
        where T : class
    {
        // The type argument is NOT checked here. Whether the container ends up
        // on the built-in default is only knowable once every module has
        // registered, so the answer belongs to host start, and reporting a
        // wrong type argument from the same place keeps one error surface
        // instead of two.
        Services.AddSingleton(new RequiredBindingRegistration(typeof(T)));
        return this;
    }

    public ITraconBuilder RequireProductionProfile(Action<TraconProductionProfileOptions>? configure = null)
    {
        var options = new TraconProductionProfileOptions();
        configure?.Invoke(options);

        // Add, not TryAdd: TryAddEnumerable deduplicates by implementation TYPE
        // and every declaration shares one, so two composition modules that both
        // declare the profile would collapse into whichever ran first. The
        // validator takes the union of the accepts, so a second declaration is
        // a no-op rather than a conflict.
        Services.AddSingleton(new ProductionProfileRegistration(options));
        return this;
    }
}
