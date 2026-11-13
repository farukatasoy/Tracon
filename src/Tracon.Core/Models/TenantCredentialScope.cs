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
