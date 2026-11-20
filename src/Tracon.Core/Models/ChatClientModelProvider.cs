using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// An <see cref="IModelProvider"/> that serves a chat client the host already has.
/// </summary>
/// <remarks>
/// <para>
/// A host that moves from a hosting framework keeps its <see cref="IChatClient"/> registrations. This provider
/// gives such a client a name and a model catalog and nothing else: it adds no tenant credential
/// (<see cref="ITenantCredentialModelProvider"/>) and no health probe, and it keeps no key that Tracon could mask.
/// The shared pipeline (the content guard, the circuit breaker, the concurrency limiter, the fallback chain, the
/// tool-call loop) is built above the client by the provider registry, as for every provider.
/// </para>
/// <para>
/// <strong>The client must not run its own tool-call loop.</strong> The registry places the content guard directly
/// above the client this provider returns, so that every turn of the loop is inspected. A client that carries a
/// <see cref="FunctionInvokingChatClient"/> runs the tool results beneath the guard, which is the path prompt
/// injection takes. The provider refuses such a client, at registration when it is given the client and the first
/// time a factory returns one.
/// </para>
/// </remarks>
internal sealed class ChatClientModelProvider : IModelProvider
{
    private readonly Func<ModelBinding, IChatClient> _create;

    /// <summary>Creates the provider.</summary>
    /// <param name="name">The provider name; <see cref="ModelBinding.Provider"/> matches it.</param>
    /// <param name="models">The model catalog; <see langword="null"/> is an empty catalog, which is a supported state.</param>
    /// <param name="create">Returns the client for a binding.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="create"/> is <see langword="null"/>.</exception>
    public ChatClientModelProvider(string name, IReadOnlyList<ModelDescriptor>? models, Func<ModelBinding, IChatClient> create)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(create);

        Name = name;
        Models = models ?? [];
        _create = create;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptor> Models { get; }

    /// <summary>Refuses a client that already runs the tool-call loop.</summary>
    /// <param name="providerName">The provider name, for the message.</param>
    /// <param name="client">The client to check.</param>
    /// <exception cref="TraconException"><paramref name="client"/> carries a <see cref="FunctionInvokingChatClient"/>.</exception>
    public static void RefuseToolCallLoop(string providerName, IChatClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (client.GetService<FunctionInvokingChatClient>() is not null)
        {
            throw new TraconException(
                $"The chat client registered for the model provider '{providerName}' already runs the tool-call loop " +
                "(it carries a FunctionInvokingChatClient). Tracon adds that loop itself, above its content guard; a second " +
                "loop inside the client runs the tool results beneath the guard, where nothing inspects them. " +
                "Build the client without UseFunctionInvocation().");
        }
    }

    /// <inheritdoc />
    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var client = _create(binding)
            ?? throw new TraconException($"The client factory of the model provider '{Name}' returned no client.");

        RefuseToolCallLoop(Name, client);

        return client;
    }
}
