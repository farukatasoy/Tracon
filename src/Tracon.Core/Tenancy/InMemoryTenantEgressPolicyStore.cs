using System.Collections.Concurrent;

namespace Tracon;

/// <summary>In-memory <see cref="ITenantEgressPolicyStore"/> implementation.</summary>
internal sealed class InMemoryTenantEgressPolicyStore : ITenantEgressPolicyStore
{
    private readonly ConcurrentDictionary<string, TenantEgressPolicy> _policies = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a new in-memory store.</summary>
    /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/> when not given.</param>
    public InMemoryTenantEgressPolicyStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public ValueTask<TenantEgressPolicy?> GetAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        _policies.TryGetValue(tenantId, out var policy);
        return ValueTask.FromResult(policy);
    }

    /// <inheritdoc />
    public ValueTask<TenantEgressPolicy> UpsertAsync(string tenantId, IReadOnlyList<string> allowedProviders, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(allowedProviders);
        cancellationToken.ThrowIfCancellationRequested();

        var policy = new TenantEgressPolicy
        {
            TenantId = tenantId,
            AllowedProviders = allowedProviders,
            UpdatedAt = _timeProvider.GetUtcNow(),
        };

        _policies[tenantId] = policy;
        return ValueTask.FromResult(policy);
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(_policies.TryRemove(tenantId, out _));
    }
}
