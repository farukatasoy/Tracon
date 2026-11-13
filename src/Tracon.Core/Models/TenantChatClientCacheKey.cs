namespace Tracon;

/// <summary>
/// Builds the key a chat client is kept under inside the cache entry of a tenant credential (BYOK).
/// </summary>
/// <remarks>
/// <para>
/// The SDK client behind a <see cref="ModelProviderCredential"/> is built once and cached by
/// <see cref="ProviderCredentialClientCache{TFactory}"/> — it owns an HTTP connection pool and is expensive. The
/// lightweight <c>IChatClient</c> wrapper a provider factory hands back from that client is cheap to allocate, but a
/// caller that builds one and holds on to it must get the same instance back for the same credential and the same
/// observable binding, not a fresh wrapper every call — the guarantee <c>ModelProviderCredentialContract</c> (in the
/// published <c>Tracon.Testing.Contracts.Xunit</c> package) requires. The setup-time (no-credential) path gives no such
/// guarantee: the four shipped factories' <c>CreateChatClient(ModelBinding)</c> methods build a fresh wrapper on every
/// call there too, and only the underlying SDK client — not the wrapper — is shared.
/// </para>
/// <para>
/// Two bindings produce the same wrapper's inputs when they name the same model and carry the same provider settings:
/// every one of the four shipped factories' <c>CreateChatClient(ModelBinding)</c> methods reads only
/// <see cref="ModelBinding.Model"/> and <see cref="ModelBinding.ProviderSettings"/> to build the wrapper — every other
/// <see cref="ModelBinding"/> field either does not affect construction or is applied later, in
/// <c>ModelProviderRegistry</c>'s pipeline.
/// </para>
/// <para>
/// The key holds no credential. The credential is what the entry the key lives in was built for, so a rotated key
/// replaces the entry and every chat client in it at once; it is never a part of a name.
/// </para>
/// </remarks>
internal static class TenantChatClientCacheKey
{
    /// <summary>Builds the key for a binding.</summary>
    /// <param name="binding">The binding the client is produced for.</param>
    /// <returns>A key stable across calls with observably identical bindings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> is <see langword="null"/>.</exception>
    public static string For(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var settings = binding.ProviderSettings.Count == 0
            ? string.Empty
            : string.Join(
                ';',
                binding.ProviderSettings
                    .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                    .Select(static pair => $"{pair.Key}={pair.Value.GetRawText()}"));

        // The length of the model is written first, so that a model name and settings that join to the same text are
        // still two keys.
        return string.Concat(
            binding.Model.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ":",
            binding.Model,
            "|",
            settings);
    }
}
