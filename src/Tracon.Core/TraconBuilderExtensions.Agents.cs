using System.Diagnostics.CodeAnalysis;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tracon;

public static partial class TraconBuilderExtensions
{
    /// <summary>
    /// Defines a declarative agent in code. The definition passes through the
    /// Tracon compiler; model and tool validation is applied.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="definition">The agent definition.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddAgent(new AgentDefinition
    ///        {
    ///            Name = "support",
    ///            Instructions = "Answer support questions from the order data.",
    ///            Model = new ModelBinding { Provider = "openai", Model = "gpt-4o-mini" },
    ///            ToolNames = ["get_order_status"],
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddAgent(this ITraconBuilder builder, AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(definition);

        builder.Services.AddSingleton(CodeAgentRegistration.FromDefinition(definition));
        return builder;
    }

    /// <summary>
    /// Defines a skill in code. A skill defined in code takes precedence over a
    /// runtime skill with the same name.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="skill">The skill to register.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// A skill is instruction text an agent loads by name. When it carries
    /// <see cref="AgentSkillDefinition.Scripts"/>, those scripts run on the
    /// server like stored ones: they need <c>AllowStoredScripts</c>, an
    /// allow-listed interpreter, and a grant pinned to their content. The hash
    /// to grant is read from <c>GET /api/skills/{name}</c>, which resolves a code
    /// skill first. A deployment that changes a script changes its hash, so the
    /// script needs a new grant before it runs again.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddSkill(new AgentSkillDefinition
    ///        {
    ///            TenantId = "default",
    ///            Name = "refund-policy",
    ///            Description = "How a refund decision is made.",
    ///            Instructions = "A refund under 100 USD is approved without review.",
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddSkill(this ITraconBuilder builder, AgentSkillDefinition skill)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(skill);

        builder.Services.AddSingleton(new CodeSkillRegistration(skill));
        return builder;
    }

    /// <summary>
    /// Defines a factory-based agent in code. How the agent is built is
    /// entirely up to the caller.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="name">The agent name.</param>
    /// <param name="factory">The factory that produces the agent.</param>
    /// <param name="description">A short description.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static ITraconBuilder AddAgent(this ITraconBuilder builder, string name, Func<IServiceProvider, AIAgent> factory, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        builder.Services.AddSingleton(CodeAgentRegistration.FromFactory(name, factory, description));
        return builder;
    }

    /// <summary>Registers a custom agent source as a singleton.</summary>
    /// <typeparam name="TSource">The source implementation type.</typeparam>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Calling this method more than once for the same source type has no effect.
    /// The source can serve global or tenant-aware agents. It must be thread-safe
    /// because the catalog calls its methods concurrently.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddAgentSource&lt;GitAgentSource&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddAgentSource<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSource>(this ITraconBuilder builder)
        where TSource : class, IAgentSource
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentSource, TSource>());
        return builder;
    }

    /// <summary>Registers a configured custom agent source as a singleton.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="source">The source instance.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static ITraconBuilder AddAgentSource(this ITraconBuilder builder, IAgentSource source)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(source);

        builder.Services.AddSingleton(source);
        return builder;
    }

    /// <summary>Registers a custom agent-source factory as a singleton.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="factory">The factory that creates the source.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static ITraconBuilder AddAgentSource(this ITraconBuilder builder, Func<IServiceProvider, IAgentSource> factory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);

        builder.Services.AddSingleton(factory);
        return builder;
    }

    /// <summary>Registers a custom agent decorator as a singleton.</summary>
    /// <typeparam name="TDecorator">The decorator implementation type.</typeparam>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Calling this method more than once for the same decorator type has no
    /// effect. The decorator joins the pipeline alongside Tracon's own
    /// (run recording, telemetry, tool approval); see
    /// <see cref="IAgentDecorator.Order"/> for where it lands. It must be
    /// thread-safe because the catalog can decorate agents concurrently.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddAgentDecorator&lt;AuditingAgentDecorator&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddAgentDecorator<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDecorator>(this ITraconBuilder builder)
        where TDecorator : class, IAgentDecorator
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentDecorator, TDecorator>());
        return builder;
    }

    /// <summary>Registers a configured custom agent decorator as a singleton.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="decorator">The decorator instance.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static ITraconBuilder AddAgentDecorator(this ITraconBuilder builder, IAgentDecorator decorator)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(decorator);

        builder.Services.AddSingleton(decorator);
        return builder;
    }

    /// <summary>Registers a custom agent-decorator factory as a singleton.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="factory">The factory that creates the decorator.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static ITraconBuilder AddAgentDecorator(this ITraconBuilder builder, Func<IServiceProvider, IAgentDecorator> factory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);

        builder.Services.AddSingleton(factory);
        return builder;
    }
}
