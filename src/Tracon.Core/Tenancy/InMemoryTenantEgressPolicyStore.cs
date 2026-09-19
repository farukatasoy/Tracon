using System.Collections.Concurrent;

namespace Tracon;

/// <summary>In-memory <see cref="ITenantEgressPolicyStore"/> implementation.</summary>
/// <remarks>
/// The tenant is keyed by its canonical form
/// (<see cref="AmbientTenantScope.Normalize"/>). A miss here is fail-OPEN:
/// <c>ModelProviderRegistry</c> applies no egress restriction at all when the
/// policy row is missing, so a tenant reached under a different letter case
/// would call a provider its own allow-list forbids. The same rule the SQL
/// stores apply, for the same reason.
/// </remarks>
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

        _policies.TryGetValue(AmbientTenantScope.Normalize(tenantId), out var policy);
        return ValueTask.FromResult(policy);
    }

    /// <inheritdoc />
    public ValueTask<TenantEgressPolicy> UpsertAsync(string tenantId, IReadOnlyList<string> allowedProviders, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(allowedProviders);
        cancellationToken.ThrowIfCancellationRequested();

        var canonical = AmbientTenantScope.Normalize(tenantId);
        var policy = new TenantEgressPolicy
        {
            TenantId = canonical,
            AllowedProviders = allowedProviders,
            UpdatedAt = _timeProvider.GetUtcNow(),
        };

        _policies[canonical] = policy;
        return ValueTask.FromResult(policy);
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(_policies.TryRemove(AmbientTenantScope.Normalize(tenantId), out _));
    }
}
