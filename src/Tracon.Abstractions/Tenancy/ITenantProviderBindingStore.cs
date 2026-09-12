namespace Tracon;

/// <summary>The store for per-tenant model provider bindings (BYOK).</summary>
/// <remarks>
/// <para>
/// This store never writes or reads a secret value, only the
/// <strong>name</strong> of the configuration key the value is read from at
/// call time.
/// </para>
/// <para>
/// <strong>Provider names are matched case-insensitively.</strong> An
/// implementation must pass every provider name it writes, and every
/// provider name it is queried with, through
/// <see cref="TenantProviderBinding.NormalizeProviderName"/>. This is not a
/// convenience: the provider registry, the tenant egress policy and the admin
/// endpoint all match the name case-insensitively, so a case-sensitive store
/// turns a binding saved as <c>"OpenAI"</c> into a MISS for an agent whose
/// definition says <c>"openai"</c> — and a miss falls through to the global
/// setup credential silently, billing the wrong tenant with no error raised.
/// <c>TenantProviderBindingStoreContract</c> proves this for every
/// implementation.
/// </para>
/// <para>
/// An implementation is registered as a <em>singleton</em> and must be safe
/// under concurrent calls from unrelated tenants.
/// </para>
/// </remarks>
public interface ITenantProviderBindingStore
{
    /// <summary>Reads a single tenant's binding for a provider.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The binding; <see langword="null"/> if none exists.</returns>
    ValueTask<TenantProviderBinding?> GetAsync(string tenantId, string providerName, CancellationToken cancellationToken = default);

    /// <summary>Lists every provider binding of a tenant.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bindings, by provider name.</returns>
    ValueTask<IReadOnlyList<TenantProviderBinding>> ListAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces a tenant's binding for a provider.</summary>
    /// <param name="binding">The binding to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    ValueTask UpsertAsync(TenantProviderBinding binding, CancellationToken cancellationToken = default);

    /// <summary>Deletes a tenant's binding for a provider.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if a binding was found and deleted.</returns>
    ValueTask<bool> DeleteAsync(string tenantId, string providerName, CancellationToken cancellationToken = default);
}
