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
}
