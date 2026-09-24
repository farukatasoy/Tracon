using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tracon;

public static partial class TraconBuilderExtensions
{
    /// <summary>Registers a custom model provider as a singleton.</summary>
    /// <typeparam name="TProvider">The provider implementation type.</typeparam>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Calling this method more than once for the same provider type has no
    /// effect. Prefer this overload when the provider has no state to
    /// configure by hand; use <see cref="AddModelProvider(ITraconBuilder, IModelProvider)"/>
    /// or the factory overload when it does.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddModelProvider&lt;OnPremiseModelProvider&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddModelProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(this ITraconBuilder builder)
        where TProvider : class, IModelProvider
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelProvider, TProvider>());
        return builder;
    }

    /// <summary>Registers a model provider.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="provider">The provider.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The shipped provider packages (<c>UseOpenAI()</c>, <c>UseAnthropic()</c>,
    /// and the rest) call this method. Register your own provider here when the
    /// model sits behind an endpoint none of them describes.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddModelProvider(new OnPremiseModelProvider(endpoint));
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddModelProvider(this ITraconBuilder builder, IModelProvider provider)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(provider);

        builder.Services.AddSingleton(provider);
        return builder;
    }

    /// <summary>Registers a model provider through a factory.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="factory">The factory that produces the provider.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static ITraconBuilder AddModelProvider(this ITraconBuilder builder, Func<IServiceProvider, IModelProvider> factory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);

        builder.Services.AddSingleton(factory);
        return builder;
    }
}
