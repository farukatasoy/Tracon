using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPrism;

/// <summary>
/// The internal twin of <c>AddJobHandler&lt;T&gt;(key)</c>, and the only path
/// allowed to register inside the <see cref="JobHandlerKeys.ReservedPrefix"/>
/// namespace.
/// </summary>
/// <remarks>
/// Kept separate from the public method rather than gated by a boolean
/// parameter: the public surface must reject a reserved key unconditionally,
/// and a consumer must not be able to reach the built-in path at all.
/// </remarks>
internal static class BuiltInJobHandlerRegistration
{
    /// <summary>Registers a built-in handler the container can construct itself.</summary>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="handlerKey">One of the <see cref="JobHandlerKeys"/> constants.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddBuiltInJobHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        this IServiceCollection services,
        string handlerKey)
        where THandler : class, IJobHandler
    {
        services.TryAddScoped<THandler>();
        services.AddSingleton(new JobHandlerRegistration(handlerKey, typeof(THandler), BuiltIn: true));

        return services;
    }

    /// <summary>
    /// Registers a built-in handler that needs an explicit factory — the
    /// built-in container does not fill in constructor parameters that carry
    /// a default value, and several handlers take dependencies that may not be
    /// registered at all.
    /// </summary>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="handlerKey">One of the <see cref="JobHandlerKeys"/> constants.</param>
    /// <param name="factory">Builds the handler from the execution's scope.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddBuiltInJobHandler<THandler>(
        this IServiceCollection services,
        string handlerKey,
        Func<IServiceProvider, THandler> factory)
        where THandler : class, IJobHandler
    {
        services.TryAddScoped(factory);
        services.AddSingleton(new JobHandlerRegistration(handlerKey, typeof(THandler), BuiltIn: true));

        return services;
    }
}
