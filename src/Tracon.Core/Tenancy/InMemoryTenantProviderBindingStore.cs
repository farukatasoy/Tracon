using System.Collections.Concurrent;

namespace Tracon;

/// <summary>In-memory <see cref="ITenantProviderBindingStore"/> implementation.</summary>
/// <remarks>
/// Both halves of the key are canonical: the provider name through
/// <see cref="TenantProviderBinding.NormalizeProviderName"/> and the
/// tenant through <see cref="AmbientTenantScope.Normalize"/>. A miss on either
/// falls through to the global setup credential with no error raised — the
/// wrong tenant is billed and nothing says so.
/// </remarks>
internal sealed class InMemoryTenantProviderBindingStore : ITenantProviderBindingStore
{
    private readonly ConcurrentDictionary<(string TenantId, string ProviderName), TenantProviderBinding> _bindings = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a new in-memory store.</summary>
    /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/> when not given.</param>
    public InMemoryTenantProviderBindingStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public ValueTask<TenantProviderBinding?> GetAsync(string tenantId, string providerName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        cancellationToken.ThrowIfCancellationRequested();

        _bindings.TryGetValue(
            (AmbientTenantScope.Normalize(tenantId), TenantProviderBinding.NormalizeProviderName(providerName)),
            out var binding);
        return ValueTask.FromResult(binding);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<TenantProviderBinding>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<TenantProviderBinding> result = _bindings.Values
            .Where(binding => string.Equals(
                binding.TenantId, AmbientTenantScope.Normalize(tenantId), StringComparison.Ordinal))
            .OrderBy(static binding => binding.ProviderName, StringComparer.Ordinal)
            .ToList();

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public ValueTask UpsertAsync(TenantProviderBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();

        var providerName = TenantProviderBinding.NormalizeProviderName(binding.ProviderName);
        var tenantId = AmbientTenantScope.Normalize(binding.TenantId);

        _bindings[(tenantId, providerName)] = binding with
        {
            TenantId = tenantId,
            ProviderName = providerName,
            UpdatedAt = _timeProvider.GetUtcNow(),
        };

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(string tenantId, string providerName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(
            _bindings.TryRemove(
                (AmbientTenantScope.Normalize(tenantId), TenantProviderBinding.NormalizeProviderName(providerName)),
                out _));
    }
}
