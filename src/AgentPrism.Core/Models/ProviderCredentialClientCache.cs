using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// Builds and caches a provider-specific client factory keyed by a resolved
/// <see cref="ModelProviderCredential"/> (phase 65, BYOK).
/// </summary>
/// <remarks>
/// <para>
/// Every provider package's SDK client is built once and shared (measured:
/// <c>OpenAIClient</c>, <c>AnthropicClient</c>, the Google GenAI
/// <c>Client</c>, and <c>AzureOpenAIClient</c> are all constructed once in
/// their factory and manage their own HTTP connection pool). A tenant
/// credential cannot reuse that shared instance — it carries a different key
/// — so a second client is built for it. This cache exists so that is done
/// <strong>once per distinct credential</strong>, not once per call: the same
/// pattern <c>OpenAINamedChatClientFactoryCache</c> already uses for named
/// OpenAI-compatible providers, generalized so all four provider packages can
/// share it (every provider package already references <c>AgentPrism.Core</c>).
/// </para>
/// <para>
/// The cache key is the credential's own value (API key + endpoint), not a
/// tenant id: two tenants that happen to share the same credential get the
/// same cached client, which is correct — the credential, not the tenant, is
/// the identity boundary here.
/// </para>
/// <para>
/// 🚨 Entries are never evicted. The number of distinct credentials is
/// bounded by the number of tenant provider bindings an operator manages
/// (a small, admin-controlled set), not by request volume, so unbounded
/// growth is not a practical concern — the same trade-off
/// <c>OpenAINamedChatClientFactoryCache</c> already makes.
/// </para>
/// </remarks>
/// <typeparam name="TFactory">The provider-specific chat client factory type.</typeparam>
public sealed class ProviderCredentialClientCache<TFactory>
    where TFactory : class
{
    private readonly ConcurrentDictionary<string, TFactory> _factories = new(StringComparer.Ordinal);

    /// <summary>Returns the factory for the given credential, building and caching it when absent.</summary>
    /// <param name="credential">The resolved credential.</param>
    /// <param name="build">Builds a factory from the credential when not already cached.</param>
    /// <returns>The cached factory.</returns>
    /// <exception cref="ArgumentNullException">A parameter is <see langword="null"/>.</exception>
    public TFactory GetOrAdd(ModelProviderCredential credential, Func<ModelProviderCredential, TFactory> build)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(build);

        var key = string.Concat(credential.ApiKey, "|", credential.Endpoint ?? string.Empty);

        return _factories.GetOrAdd(key, _ => build(credential));
    }
}
