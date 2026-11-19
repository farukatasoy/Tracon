namespace Tracon;

/// <summary>
/// Names the tenant binding a <see cref="ModelProviderCredential"/> was resolved for, so a client built from the
/// credential can be kept under that name instead of under the credential's value.
/// </summary>
/// <remarks>
/// <para>
/// The name is the tenant and the provider of the binding. Both are written in the closed alphabet of the ASCII
/// letters, the digits, <c>_</c>, <c>.</c> and <c>-</c> (a store refuses anything else), so the visible <c>/</c>
/// between them cannot occur inside either and two different bindings never share a name.
/// </para>
/// <para>
/// The name carries no secret: it is safe in a log line and in a dictionary key. The credential value is the thing
/// the cache <em>compares</em> under this name, never the thing it keys on.
/// </para>
/// </remarks>
internal static class TenantCredentialScope
{
    private const string Separator = "/";

    private const string SetupPrefix = "setup:";

    /// <summary>Returns the name of the binding of a tenant and a provider.</summary>
    /// <param name="tenantId">The tenant, as the binding store holds it.</param>
    /// <param name="providerName">The provider name, as the binding store holds it.</param>
    /// <returns>The name.</returns>
    /// <exception cref="ArgumentException">A parameter is <see langword="null"/> or blank.</exception>
    public static string For(string tenantId, string providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        return string.Concat(tenantId, Separator, providerName);
    }

    /// <summary>Returns a copy of a credential that carries the name of the binding it was resolved for.</summary>
    /// <param name="credential">The resolved credential.</param>
    /// <param name="tenantId">The tenant of the binding.</param>
    /// <param name="providerName">The provider name of the binding.</param>
    /// <returns>The credential with its binding name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="credential"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A name is <see langword="null"/> or blank.</exception>
    public static ModelProviderCredential Attach(ModelProviderCredential credential, string tenantId, string providerName)
    {
        ArgumentNullException.ThrowIfNull(credential);

        return credential with { BindingScope = For(tenantId, providerName) };
    }

    /// <summary>
    /// Returns a copy of a credential that carries the scope of the key and the address a provider was set up with.
    /// </summary>
    /// <param name="credential">The snapshot of the setup key and address.</param>
    /// <param name="providerName">The provider name.</param>
    /// <returns>The credential with its setup scope.</returns>
    /// <remarks>
    /// The scope has a <c>:</c> in it, and a tenant name cannot (see the alphabet above), so a tenant called
    /// <c>setup</c> never gets the client of the setup key. A credential with a scope is kept under it and replaced
    /// when its value changes; one without would be kept under its value for ever.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="credential"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="providerName"/> is <see langword="null"/> or blank.</exception>
    public static ModelProviderCredential AttachSetup(ModelProviderCredential credential, string providerName)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        return credential with { BindingScope = SetupPrefix + providerName };
    }

    /// <summary>Tells whether a credential is the snapshot of the key and the address a provider was set up with.</summary>
    /// <param name="credential">The credential.</param>
    /// <returns><see langword="true"/> for a credential made by <see cref="AttachSetup"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="credential"/> is <see langword="null"/>.</exception>
    public static bool IsSetup(ModelProviderCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        return credential.BindingScope is { } scope && scope.StartsWith(SetupPrefix, StringComparison.Ordinal);
    }

    /// <summary>Returns the name of the binding a credential was resolved for.</summary>
    /// <param name="credential">The credential.</param>
    /// <returns>The name, or <see langword="null"/> when the credential did not come from a tenant binding.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="credential"/> is <see langword="null"/>.</exception>
    public static string? Of(ModelProviderCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        return credential.BindingScope;
    }
}
