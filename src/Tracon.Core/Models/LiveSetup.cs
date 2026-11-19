using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// The clients a provider builds from the key and the address it was set up with, kept under the setup scope and
/// replaced when the key or the address changes.
/// </summary>
/// <typeparam name="TFactory">The provider's chat client factory.</typeparam>
/// <remarks>
/// <para>
/// One instance serves everything that sends the setup key of one provider: both OpenAI surfaces, the chat clients, the
/// image and speech clients, the live voice sessions. The same snapshot gives the same factory, so one key is one SDK
/// client and one connection pool, and a rotation is seen by all of them at once.
/// </para>
/// <para>
/// A replaced factory is not disposed (see <see cref="ProviderCredentialClientCache{TFactory}"/>): a call that began
/// before the rotation still holds the client it was given.
/// </para>
/// </remarks>
internal sealed class LiveSetup<TFactory>
    where TFactory : class
{
    private readonly ProviderCredentialClientCache<TFactory> _cache = new();
    private readonly Func<ModelProviderCredential, TFactory> _build;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="source">The source of the key and the address.</param>
    /// <param name="initial">The factory the host started with; it serves the first snapshot instead of a second one.</param>
    /// <param name="build">Builds the factory of a snapshot the cache has not seen.</param>
    /// <exception cref="ArgumentNullException">A parameter is <see langword="null"/>.</exception>
    internal LiveSetup(SetupCredentialSource source, TFactory initial, Func<ModelProviderCredential, TFactory> build)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(build);

        Source = source;
        Initial = initial;
        _build = build;

        // The factory the host started with serves the snapshot it was built for. Without this the first call would
        // build a second client for the same key.
        _cache.GetOrAdd(source.Current(), _ => initial);
    }

    /// <summary>Gets the source of the key and the address.</summary>
    internal SetupCredentialSource Source { get; }

    /// <summary>Gets the factory the host started with.</summary>
    internal TFactory Initial { get; }

    /// <summary>Gets the number of factories the cache holds.</summary>
    internal int CachedCount => _cache.Count;

    /// <summary>Reads the key and the address.</summary>
    /// <returns>The snapshot of one call.</returns>
    internal ModelProviderCredential Snapshot() => Source.Current();

    /// <summary>Returns the factory of the current key and address.</summary>
    /// <returns>The factory.</returns>
    internal TFactory Current() => For(Source.Current());

    /// <summary>Returns the factory of a snapshot.</summary>
    /// <param name="snapshot">The snapshot a call read once.</param>
    /// <returns>The factory; the same instance for the same snapshot.</returns>
    internal TFactory For(ModelProviderCredential snapshot)
        => _cache.GetOrAdd(snapshot, _build);

    /// <summary>Returns the chat client of a snapshot, a model and its settings.</summary>
    /// <param name="snapshot">The snapshot a call read once.</param>
    /// <param name="binding">The model binding.</param>
    /// <param name="create">Creates the chat client from the factory.</param>
    /// <returns>The same client for the same snapshot and binding.</returns>
    internal IChatClient ChatClient(ModelProviderCredential snapshot, ModelBinding binding, Func<TFactory, ModelBinding, IChatClient> create)
        => _cache.GetOrAddChatClient(snapshot, binding, _build, create);
}
