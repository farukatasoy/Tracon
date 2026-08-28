namespace AgentPrism;

/// <summary>
/// Builds the cache key for an <see cref="Microsoft.Extensions.AI.IChatClient"/>
/// produced for a tenant credential (BYOK).
/// </summary>
/// <remarks>
/// <para>
/// The SDK client behind a <see cref="ModelProviderCredential"/> is built once
/// and cached by <see cref="ProviderCredentialClientCache{TFactory}"/> — it
/// owns an HTTP connection pool and is expensive. The lightweight
/// <c>IChatClient</c> wrapper a provider factory hands back from that client
/// is cheap to allocate, but a caller that builds one and holds on to it must
/// get the same instance back for the same credential and the same
/// observable binding, not a fresh wrapper every call — the guarantee
/// <c>ModelProviderCredentialContract</c> (in the published
/// <c>AgentPrism.Testing.Contracts.Xunit</c> package) requires. The
/// setup-time (no-credential) path gives no such guarantee: the four shipped
/// factories' <c>CreateChatClient(ModelBinding)</c> methods build a fresh
/// wrapper on every call there too, and only the underlying SDK client — not
/// the wrapper — is shared.
/// </para>
/// <para>
/// Two bindings produce the same wrapper's inputs when they name the same
/// model and carry the same provider settings: every one of the four shipped
/// factories' <c>CreateChatClient(ModelBinding)</c> methods reads only
/// <see cref="ModelBinding.Model"/> and <see cref="ModelBinding.ProviderSettings"/>
/// to build the wrapper — every other <see cref="ModelBinding"/> field either
/// does not affect construction or is applied later, in
/// <c>ModelProviderRegistry</c>'s pipeline.
/// </para>
/// <para>
/// Entries in the cache this key is used with are never evicted, the same
/// trade-off <see cref="ProviderCredentialClientCache{TFactory}"/> documents:
/// the number of distinct (credential, model, settings) combinations is
/// bounded by what an operator's tenants actually use, not by request volume.
/// </para>
/// </remarks>
public static class TenantChatClientCacheKey
{
    /// <summary>Builds the cache key for a credential and a binding.</summary>
    /// <param name="credential">The resolved tenant credential.</param>
    /// <param name="binding">The binding the client is produced for.</param>
    /// <returns>A key stable across calls with observably identical inputs.</returns>
    /// <exception cref="ArgumentNullException">A parameter is <see langword="null"/>.</exception>
    public static string For(ModelProviderCredential credential, ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(binding);

        var settings = binding.ProviderSettings.Count == 0
            ? string.Empty
            : string.Join(
                ';',
                binding.ProviderSettings
                    .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                    .Select(static pair => $"{pair.Key}={pair.Value.GetRawText()}"));

        return string.Concat(credential.ApiKey, "|", credential.Endpoint, "|", binding.Model, "|", settings);
    }
}
