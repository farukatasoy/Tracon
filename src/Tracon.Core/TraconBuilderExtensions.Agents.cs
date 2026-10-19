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

    /// <summary>Registers an agent extension as a singleton.</summary>
    /// <typeparam name="TExtension">The extension implementation type.</typeparam>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// A definition selects the extension by <see cref="IAgentExtension.Name"/> in
    /// <see cref="AgentDefinition.ExtensionNames"/>. Calling this method more than
    /// once for the same type has no effect; two types that report the same name are
    /// rejected when the compiler is first used.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddAgentExtension&lt;PolicyContextExtension&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddAgentExtension<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TExtension>(this ITraconBuilder builder)
        where TExtension : class, IAgentExtension
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentExtension, TExtension>());
        return builder;
    }

    /// <summary>Registers a configured agent extension.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="extension">The extension instance.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="extension"/> is <see langword="null"/>.</exception>
    public static ITraconBuilder AddAgentExtension(this ITraconBuilder builder, IAgentExtension extension)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(extension);

        builder.Services.AddSingleton(extension);
        return builder;
    }

    /// <summary>
    /// Registers an agent extension from a delegate. A definition selects it by
    /// <paramref name="name"/> in <see cref="AgentDefinition.ExtensionNames"/>.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="name">
    /// The extension name: 1 to 128 characters of lowercase ASCII letters, digits, <c>.</c>,
    /// <c>_</c> or <c>-</c>, unique among the registered extensions.
    /// </param>
    /// <param name="configure">
    /// Changes the options of the agent that is being compiled, or adds a wrapper. It runs
    /// when an agent that names the extension is compiled, not on every run.
    /// </param>
    /// <param name="description">
    /// What the extension does, in one sentence. The console shows it next to the name and
    /// <c>GET /api/agent-extensions</c> returns it to every reader, so it must not carry a
    /// secret. <see langword="null"/> shows the name alone.
    /// </param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <remarks>
    /// <para>
    /// The escape door for a Microsoft Agent Framework option Tracon does not model.
    /// A hand-built agent in <c>AddAgent(name, factory)</c> bypasses the model ring, the
    /// tenant's provider credential, egress and cost; an extension keeps all of them
    /// because the agent still goes through the compiler. The compiler refuses a change
    /// to a member the definition or a tenant and security decision owns (the tool chain,
    /// the tool approval settings, the chat history provider, file access, the
    /// working-directory skill scan and the model parameters among them). It does not look
    /// inside a provider or a wrapper the extension adds.
    /// </para>
    /// <example>
    /// Turns on Microsoft Agent Framework's message injection for the agents that select it:
    /// <code>
    /// builder.AddTracon()
    ///        .AddAgentExtension(
    ///            "message-injection",
    ///            context =>
    ///            {
    ///                if (context.ChatAgentOptions is { } options)
    ///                {
    ///                    options.EnableMessageInjection = true;
    ///                }
    ///            },
    ///            description: "Lets a caller inject a message into a running turn.");
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddAgentExtension(
        this ITraconBuilder builder,
        string name,
        Action<AgentExtensionContext> configure,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddSingleton<IAgentExtension>(new DelegateAgentExtension(name, configure, description));
        return builder;
    }
}
