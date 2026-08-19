namespace AgentPrism;

/// <summary>The store for per-tenant model provider bindings (BYOK, phase 65).</summary>
/// <remarks>
/// 🚨 This store never writes or reads a secret value, only the
/// <strong>name</strong> of the configuration key the value is read from at
/// call time (decision K-059). See docs/65-KIRACI-SAGLAYICI-ANAHTARLARI.md.
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
