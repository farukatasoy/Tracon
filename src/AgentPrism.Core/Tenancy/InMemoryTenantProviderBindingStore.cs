using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>In-memory <see cref="ITenantProviderBindingStore"/> implementation.</summary>
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

        _bindings.TryGetValue((tenantId, TenantProviderBinding.NormalizeProviderName(providerName)), out var binding);
        return ValueTask.FromResult(binding);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<TenantProviderBinding>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        IReadOnlyList<TenantProviderBinding> result = _bindings.Values
            .Where(binding => string.Equals(binding.TenantId, tenantId, StringComparison.Ordinal))
            .OrderBy(static binding => binding.ProviderName, StringComparer.Ordinal)
            .ToList();

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public ValueTask UpsertAsync(TenantProviderBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var providerName = TenantProviderBinding.NormalizeProviderName(binding.ProviderName);

        _bindings[(binding.TenantId, providerName)] = binding with
        {
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

        return ValueTask.FromResult(
            _bindings.TryRemove((tenantId, TenantProviderBinding.NormalizeProviderName(providerName)), out _));
    }
}
