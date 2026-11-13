using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Builds and keeps the provider-specific client factory and the chat clients of each tenant binding (BYOK), under
/// the name of the binding and not under the value of its credential.
/// </summary>
/// <remarks>
/// <para>
/// Every provider package's SDK client is built once and shared (measured:
/// <c>OpenAIClient</c> (also the Azure provider's, pointed at the v1 endpoint),
/// <c>AnthropicClient</c>, and the Google GenAI <c>Client</c> are all
/// constructed once in their factory and manage their own HTTP connection pool). A tenant
/// credential cannot reuse that shared instance — it carries a different key
/// — so a second client is built for it. This cache exists so that is done
/// <strong>once per credential of a binding</strong>, not once per call: the same
/// pattern <c>OpenAINamedChatClientFactoryCache</c> already uses for named
/// OpenAI-compatible providers, generalized so all four provider packages can
/// share it (every provider package already references <c>Tracon.Core</c>).
/// </para>
/// <para>
/// <strong>The key is the binding, the credential is what is compared.</strong> A credential that was resolved from a
/// tenant binding carries the name of the binding (<see cref="TenantCredentialScope"/>: tenant and provider). The
/// cache holds one entry under that name, and the entry remembers a fingerprint of the credential it was built for.
/// A call with the same credential gets the entry; a call with another credential (the operator rotated the key, or
/// changed the endpoint) builds a new entry and <strong>replaces</strong> the old one. A rotated key therefore leaves
/// one entry, not one more, and the old key is held by nothing the cache keeps. Two tenants that happen to hold the
/// same key value have two bindings and so two entries: a client is never shared across the tenant boundary.
/// </para>
/// <para>
/// A replaced entry is <strong>not disposed</strong>. A call that began before the rotation still holds the client it
/// was given, and a client that is disposed under a running call fails that call. The collector releases the entry
/// (its factory, its chat clients and the HTTP pool they own) once the last such call has finished. The cost is the
/// delay of a rotation, which an operator performs rarely; the gain is that no call is cut short by one.
/// </para>
/// <para>
/// A credential that names no binding (a caller that built the credential itself and asked a provider directly) has
/// no owner to replace, so it is kept by its value, as it always was. Its key in the cache is a fingerprint of the
/// value, never the value, and such entries are never replaced.
/// </para>
/// <para>
/// The cache never holds the credential value itself, only a SHA-256 fingerprint of it: the factory it builds holds
/// the key it needs to call the provider, and that is the one place the value lives. Entries are not evicted by
/// time or size. The number of bindings is what an operator manages (a small, admin-controlled set), not request
/// volume, so unbounded growth is not a practical concern — the same trade-off
/// <c>OpenAINamedChatClientFactoryCache</c> already makes. A binding an operator deletes keeps its last entry until
/// the process ends.
/// </para>
/// </remarks>
/// <typeparam name="TFactory">The provider-specific chat client factory type.</typeparam>
internal sealed class ProviderCredentialClientCache<TFactory>
    where TFactory : class
{
    private readonly ConcurrentDictionary<string, Slot> _slots = new(StringComparer.Ordinal);

    /// <summary>Gets the number of entries the cache holds: one for each binding, and one for each credential that names none.</summary>
    internal int Count => _slots.Values.Count(static slot => Volatile.Read(ref slot.Current) is not null);

    /// <summary>Gets the names the entries are kept under: a binding name, or a fingerprint. Never a credential value.</summary>
    internal IReadOnlyCollection<string> Keys => [.. _slots.Where(static pair => Volatile.Read(ref pair.Value.Current) is not null).Select(static pair => pair.Key)];

    /// <summary>
    /// Returns the factory for the given credential, building it when the binding has none for this credential, and
    /// replacing the factory the binding held when its credential has changed.
    /// </summary>
    /// <param name="credential">The resolved credential.</param>
    /// <param name="build">Builds a factory from the credential when none is cached for it.</param>
    /// <returns>The cached factory.</returns>
    /// <exception cref="ArgumentNullException">A parameter is <see langword="null"/>.</exception>
    public TFactory GetOrAdd(ModelProviderCredential credential, Func<ModelProviderCredential, TFactory> build)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(build);

        return Resolve(credential, build).Factory;
    }

    /// <summary>
    /// Returns the chat client of a binding for a model and its provider settings, creating it from the factory of
    /// the credential when none exists.
    /// </summary>
    /// <param name="credential">The resolved credential.</param>
    /// <param name="binding">The model binding the client is for.</param>
    /// <param name="build">Builds the factory from the credential when none is cached for it.</param>
    /// <param name="create">Creates the chat client from the factory.</param>
    /// <returns>The same client for the same credential, model and settings.</returns>
    /// <remarks>
    /// The chat clients live inside the entry of the credential they were built from, so a rotation replaces the
    /// factory and every chat client built from it together, and none of them keeps the old key reachable.
    /// </remarks>
    /// <exception cref="ArgumentNullException">A parameter is <see langword="null"/>.</exception>
    public IChatClient GetOrAddChatClient(
        ModelProviderCredential credential,
        ModelBinding binding,
        Func<ModelProviderCredential, TFactory> build,
        Func<TFactory, ModelBinding, IChatClient> create)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(create);

        var entry = Resolve(credential, build);

        return entry.ChatClients.GetOrAdd(TenantChatClientCacheKey.For(binding), _ => create(entry.Factory, binding));
    }

    private Entry Resolve(ModelProviderCredential credential, Func<ModelProviderCredential, TFactory> build)
    {
        var fingerprint = Fingerprint(credential);
        var scope = TenantCredentialScope.Of(credential);

        // A binding is kept under its name. A credential that names none is kept under its own fingerprint, so
        // that two of them never replace one another. The prefixes keep the two kinds of name apart.
        var slot = _slots.GetOrAdd(scope is null ? "credential:" + fingerprint : "binding:" + scope, static _ => new Slot());

        if (Volatile.Read(ref slot.Current) is { } current && string.Equals(current.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return current;
        }

        // The compare and the replace are one step, so that two callers that bring different credentials for one
        // binding never both leave an entry behind, and two callers that bring the same one build once. Building a
        // client opens no connection; the lock is held for the time of a constructor.
        lock (slot.Gate)
        {
            if (slot.Current is { } held && string.Equals(held.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return held;
            }

            // The entry this replaces is dropped, not disposed: see the remarks of the type.
            var replacement = new Entry(fingerprint, build(credential));
            Volatile.Write(ref slot.Current, replacement);

            return replacement;
        }
    }

    // The length of the key is written first, so that a key and an endpoint that join to the same text are still two
    // credentials. A missing endpoint and an empty one are the same credential, as they are to every provider.
    private static string Fingerprint(ModelProviderCredential credential)
    {
        var text = string.Concat(
            credential.ApiKey.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ":",
            credential.ApiKey,
            "|",
            credential.Endpoint ?? string.Empty);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private sealed class Slot
    {
        public readonly Lock Gate = new();

        public Entry? Current;
    }

    private sealed class Entry(string fingerprint, TFactory factory)
    {
        public string Fingerprint { get; } = fingerprint;

        public TFactory Factory { get; } = factory;

        public ConcurrentDictionary<string, IChatClient> ChatClients { get; } = new(StringComparer.Ordinal);
    }
}
