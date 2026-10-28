using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

public static partial class TraconBuilderExtensions
{
    /// <summary>Registers a tool.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="tool">The tool to register.</param>
    /// <param name="configure">Configures the tool metadata.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The AOT-safe overload: the caller supplies the built
    /// <see cref="AIFunction"/>, so no reflection is involved.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddTool(refundTool, options => options.RequiresApproval = true);
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddTool(this ITraconBuilder builder, AIFunction tool, Action<ToolRegistrationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ToolRegistrationOptions();
        configure(options);
        builder.Services.AddSingleton(ToolRegistrationMapping.FromOptions(tool, options));
        return builder;
    }

    /// <summary>Registers a tool with default metadata.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="tool">The tool to register.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static ITraconBuilder AddTool(this ITraconBuilder builder, AIFunction tool)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(tool);
        return builder.AddTool(tool, static _ => { });
    }

    /// <summary>Registers a tool that the container builds, so the tool can take its dependencies at setup.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="factory">Builds the tool from the host's service provider.</param>
    /// <param name="configure">Configures the tool metadata.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/>, <paramref name="factory"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// A tool does not get dependency injection at call time: Microsoft Agent Framework gives it an
    /// empty service provider. A tool that needs a service takes it when it is built. This method is
    /// the place: the factory gets the host's service provider and builds the tool around what it
    /// resolves. <paramref name="configure"/> runs now, when you register. The factory runs once, when the
    /// host builds the tool registry, which is at start. Both singletons: the tool is shared by every run
    /// and every tenant. Use <see cref="AddScopedTool(ITraconBuilder, AIFunction, Action{ToolRegistrationOptions})"/>
    /// for a tool that needs a fresh scope on every call.
    /// </para>
    /// <para>
    /// The factory must not ask for <see cref="IToolRegistry"/> or for a service that needs it: the registry
    /// is being built from the result of the factory. Tracon stops the host with a message that names
    /// this. A factory that returns <see langword="null"/>, or a tool with an invalid name, stops it as well.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddTool(
    ///            provider =>
    ///            {
    ///                var orders = provider.GetRequiredService&lt;IOrderRepository&gt;();
    ///                return AIFunctionFactory.Create(
    ///                    (string orderId) => orders.GetAsync(orderId),
    ///                    "get_order_status");
    ///            },
    ///            options => options.RequiredPermission = "orders.read");
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddTool(
        this ITraconBuilder builder,
        Func<IServiceProvider, AIFunction> factory,
        Action<ToolRegistrationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ToolRegistrationOptions();
        configure(options);

        // The factory needs the FINAL provider, so the registration is built lazily, as AddScopedTool does.
        builder.Services.AddSingleton<TraconToolRegistration>(provider => ToolRegistrationMapping.FromOptions(
            factory(provider) ?? throw new TraconException(
                "The factory passed to AddTool returned null. A tool factory returns the tool it built."),
            options));
        return builder;
    }

    /// <summary>Registers a tool that the container builds, with default metadata.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="factory">Builds the tool from the host's service provider.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// See <see cref="AddTool(ITraconBuilder, Func{IServiceProvider, AIFunction}, Action{ToolRegistrationOptions})"/>.
    /// </remarks>
    public static ITraconBuilder AddTool(this ITraconBuilder builder, Func<IServiceProvider, AIFunction> factory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        return builder.AddTool(factory, static _ => { });
    }

    /// <summary>Registers a tool that runs inside its own dependency-injection scope on every call.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="tool">The tool to register.</param>
    /// <param name="configure">Configures the tool metadata.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Identical in shape to <see cref="AddTool(ITraconBuilder, AIFunction, Action{ToolRegistrationOptions})"/> —
    /// the one difference is the word "scoped", and its meaning is exactly
    /// this: every call opens a fresh <see cref="IServiceScope"/>, exposes it
    /// through <see cref="AIFunctionArguments.Services"/>, and closes it as
    /// soon as the call finishes. Microsoft Agent Framework otherwise passes
    /// an empty provider there — a dependency resolved from
    /// <c>AIFunctionArguments.Services</c> without this method throws or
    /// returns nothing, it is never silently wrong.
    /// </para>
    /// <para>
    /// Use this for a tool that needs a repository, a <c>DbContext</c>, or
    /// any other per-call dependency. Two concurrent calls never share a
    /// scope.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddScopedTool(AIFunctionFactory.Create(
    ///            async (string orderId, AIFunctionArguments arguments) =>
    ///            {
    ///                var orders = arguments.Services!.GetRequiredService&lt;IOrderRepository&gt;();
    ///                return await orders.GetAsync(orderId);
    ///            },
    ///            "get_order"),
    ///            options => options.RequiredPermission = "orders.read");
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddScopedTool(this ITraconBuilder builder, AIFunction tool, Action<ToolRegistrationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ToolRegistrationOptions();
        configure(options);

        // The scope factory can only be resolved from the FINAL provider, so
        // the registration itself is built lazily through a factory, unlike
        // AddTool's eager instance registration.
        builder.Services.AddSingleton<TraconToolRegistration>(provider => ToolRegistrationMapping.FromOptions(
            new ScopedAIFunction(tool, provider.GetRequiredService<IServiceScopeFactory>()),
            options));
        return builder;
    }

    /// <summary>Registers a tool that runs inside its own dependency-injection scope on every call, with default metadata.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="tool">The tool to register.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static ITraconBuilder AddScopedTool(this ITraconBuilder builder, AIFunction tool)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(tool);
        return builder.AddScopedTool(tool, static _ => { });
    }

    /// <summary>Builds and registers a tool from a method.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="method">The method to expose as a tool.</param>
    /// <param name="name">The tool name.</param>
    /// <param name="description">The tool description.</param>
    /// <param name="configure">Configures the tool metadata.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode("Building a tool from a method uses reflection; type information may be lost in trimmed applications.")]
    [RequiresDynamicCode("Building a tool from a method may require code generation at runtime.")]
    public static ITraconBuilder AddTool(this ITraconBuilder builder, Delegate method, string? name = null, string? description = null, Action<ToolRegistrationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(method);
        return configure is null
            ? builder.AddTool(AIFunctionFactory.Create(method, name, description))
            : builder.AddTool(AIFunctionFactory.Create(method, name, description), configure);
    }

    /// <summary>
    /// Registers, as tools, the methods on a type that are marked with
    /// <see cref="TraconToolAttribute"/>.
    /// </summary>
    /// <typeparam name="T">The type to scan.</typeparam>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">
    /// <typeparamref name="T"/> has no marked method, or a marked method cannot
    /// be converted to a tool.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Marking is an explicit choice: every new method added to the class is
    /// not automatically exposed to agents. Static methods bind directly; for
    /// instance methods, the owning object is resolved from the service
    /// provider at call time.
    /// </para>
    /// <para>
    /// <strong>A static class cannot be a type argument</strong> (a C# rule).
    /// If your tools live in a <c>static class</c>, use the
    /// <see cref="AddToolsFrom(ITraconBuilder, Type)"/> overload instead.
    /// </para>
    /// <para>
    /// This method uses reflection and is not safe under trimming or native AOT
    /// scenarios. Applications targeting AOT should use the
    /// <see cref="AddTool(ITraconBuilder, AIFunction, Action{ToolRegistrationOptions})"/> overload instead.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddToolsFrom&lt;OrderTools&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    [RequiresUnreferencedCode("Tool scanning uses reflection; method information may be lost in trimmed applications.")]
    [RequiresDynamicCode("Tool scanning may require code generation at runtime.")]
    public static ITraconBuilder AddToolsFrom<T>(this ITraconBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddToolsFrom(typeof(T));
    }

    /// <summary>
    /// Registers, as tools, the methods on a type that are marked with
    /// <see cref="TraconToolAttribute"/>.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="type">The type to scan. May be a <c>static class</c>.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="type"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">
    /// <paramref name="type"/> has no marked method, or a marked method cannot
    /// be converted to a tool.
    /// </exception>
    /// <remarks>
    /// A static class cannot be a type argument under C# rules; this overload
    /// makes the <c>AddToolsFrom(typeof(OrderTools))</c> form possible.
    /// </remarks>
    [RequiresUnreferencedCode("Tool scanning uses reflection; method information may be lost in trimmed applications.")]
    [RequiresDynamicCode("Tool scanning may require code generation at runtime.")]
    public static ITraconBuilder AddToolsFrom(this ITraconBuilder builder, Type type)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(type);

        foreach (var registration in ToolMethodScanner.Scan(type))
        {
            builder.Services.AddSingleton(registration);
        }

        return builder;
    }

    /// <summary>Registers a tool the model provider runs on its own side: a web search, a code interpreter or a file search.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="name">
    /// The name an agent definition selects the tool by in <see cref="AgentDefinition.ToolNames"/>.
    /// It is unique among function tools and hosted tools.
    /// </param>
    /// <param name="kind">The kind of tool the factory builds.</param>
    /// <param name="factory">
    /// Builds the tool for the agent being compiled. It must return the type that matches
    /// <paramref name="kind"/> (<c>HostedWebSearchTool</c>, <c>HostedCodeInterpreterTool</c> or
    /// <c>HostedFileSearchTool</c>) and a new instance on every call.
    /// </param>
    /// <param name="description">The description the agent editor shows, or <see langword="null"/>.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/>, <paramref name="name"/> or <paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined kind.</exception>
    /// <exception cref="TraconException">The tool name does not follow the tool naming rules.</exception>
    /// <remarks>
    /// <para>
    /// A hosted tool is code, as every tool is: the definition selects it by name, and its default is
    /// off. The provider runs the tool inside the model call, so Tracon puts no timeout, approval or
    /// permission in front of it, and what the tool reads (the web, a vector store, the data a code
    /// interpreter is given) goes to the provider the model call already goes to. Each call is billed by the
    /// provider: set <c>Tracon:Pricing:HostedTools:{provider}:{kind}:PerCall</c> so the cost reaches the run's
    /// budget, the tenant's quota and the <c>run.completed</c> webhook.
    /// </para>
    /// <para>
    /// The provider's API surface decides whether the kind runs. A definition that selects a kind the
    /// surface drops does not compile; the agent validation names the tool and the provider.
    /// An agent selects at most one hosted tool of each kind.
    /// </para>
    /// <para>
    /// The factory runs on every run of an agent that selects the tool, for the tenant of the run; keep it cheap.
    /// A value that belongs to the tenant, such as the identifier of the tenant's own vector store, is read
    /// there. An identifier is not a <c>secret</c>; never put a credential into a hosted tool.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddHostedTool(
    ///            "docs_search",
    ///            HostedToolKind.FileSearch,
    ///            context => new HostedFileSearchTool
    ///            {
    ///                Inputs = [new HostedVectorStoreContent(
    ///                    context.Services.GetRequiredService&lt;IConfiguration&gt;()["VectorStores:Docs"]!)],
    ///            },
    ///            description: "Searches the product documentation.");
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddHostedTool(
        this ITraconBuilder builder,
        string name,
        HostedToolKind kind,
        Func<HostedToolContext, AITool> factory,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(factory);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The hosted tool kind is not defined.");
        }

        if (!ToolNameRules.IsValid(name))
        {
            throw new TraconException($"Tool name '{name}' is invalid. {ToolNameRules.Description}");
        }

        builder.Services.AddSingleton(new HostedToolRegistration(name, kind, factory, description));

        return builder;
    }
}
