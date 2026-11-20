using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
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

    /// <summary>Registers a chat client you already have as a model provider.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="name">The provider name. <see cref="ModelBinding.Provider"/> matches it, ignoring letter case.</param>
    /// <param name="client">
    /// The client, shared by every binding of the provider and called from many threads. Tracon never disposes it.
    /// </param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    /// <exception cref="TraconException"><paramref name="client"/> already runs the tool-call loop.</exception>
    /// <remarks>
    /// <para>
    /// The egress policy applies to the name. A tenant's own key does <strong>not</strong> reach the client (the provider
    /// is not an <see cref="ITenantCredentialModelProvider"/>): a run of a tenant that has its own key for this provider
    /// fails and does not fall back to yours.
    /// </para>
    /// <para>
    /// Build the client without <c>UseFunctionInvocation()</c>. Tracon adds the tool-call loop above its content guard,
    /// and a loop inside your client runs the tool results beneath it, so such a client is refused.
    /// </para>
    /// <example>
    /// <code>
    /// IChatClient client = new OnPremiseChatClient(endpoint);
    ///
    /// builder.AddTracon()
    ///        .AddChatClientProvider("on-premise", client);
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddChatClientProvider(this ITraconBuilder builder, string name, IChatClient client)
        => AddChatClientProvider(builder, name, client, models: null);

    /// <summary>Registers a chat client as a model provider, with a model catalog.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="name">The provider name.</param>
    /// <param name="client">The client. See <see cref="AddChatClientProvider(ITraconBuilder, string, IChatClient)"/>.</param>
    /// <param name="models">The model catalog, which feeds the console and the cost report; not an allow list. <see langword="null"/> is empty.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    /// <exception cref="TraconException"><paramref name="client"/> already runs the tool-call loop.</exception>
    public static ITraconBuilder AddChatClientProvider(
        this ITraconBuilder builder,
        string name,
        IChatClient client,
        IReadOnlyList<ModelDescriptor>? models)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(client);

        ChatClientModelProvider.RefuseToolCallLoop(name, client);

        return builder.AddModelProvider(new ChatClientModelProvider(name, models, _ => client));
    }

    /// <summary>Registers a chat client that a factory builds as a model provider.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="name">The provider name.</param>
    /// <param name="factory">
    /// Returns the client for a binding, from the host's services. It may be called many times and from many threads;
    /// return a shared client. Tracon never disposes what it returns.
    /// </param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    /// <remarks>
    /// The rules of <see cref="AddChatClientProvider(ITraconBuilder, string, IChatClient)"/> hold. A client with its own
    /// tool-call loop is refused the first time the factory returns it, as a compilation error of the agent that asked.
    /// </remarks>
    public static ITraconBuilder AddChatClientProvider(
        this ITraconBuilder builder,
        string name,
        Func<IServiceProvider, ModelBinding, IChatClient> factory)
        => AddChatClientProvider(builder, name, factory, models: null);

    /// <summary>Registers a chat client that a factory builds as a model provider, with a model catalog.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="name">The provider name.</param>
    /// <param name="factory">Returns the client for a binding. See <see cref="AddChatClientProvider(ITraconBuilder, string, Func{IServiceProvider, ModelBinding, IChatClient})"/>.</param>
    /// <param name="models">The model catalog, which feeds the console and the cost report; not an allow list. <see langword="null"/> is empty.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    public static ITraconBuilder AddChatClientProvider(
        this ITraconBuilder builder,
        string name,
        Func<IServiceProvider, ModelBinding, IChatClient> factory,
        IReadOnlyList<ModelDescriptor>? models)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        return builder.AddModelProvider(provider => new ChatClientModelProvider(name, models, binding => factory(provider, binding)));
    }
}
