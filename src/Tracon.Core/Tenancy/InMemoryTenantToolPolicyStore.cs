using System.Collections.Concurrent;

namespace Tracon;

/// <summary>In-memory <see cref="ITenantToolPolicyStore"/> implementation.</summary>
/// <remarks>
/// The tenant is keyed by its canonical form (<see cref="AmbientTenantScope.Normalize"/>). A miss here is
/// fail-OPEN while <c>Tracon:Tools:RequireTenantPolicy</c> is off: the policy gate applies no restriction when
/// the policy row is missing, so a tenant reached under a different letter case would run a tool its own policy
/// forbids. The SQL store applies the same rule, for the same reason.
/// </remarks>
internal sealed class InMemoryTenantToolPolicyStore : ITenantToolPolicyStore
{
    private readonly ConcurrentDictionary<string, TenantToolPolicy> _policies = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a new in-memory store.</summary>
    /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/> when not given.</param>
    public InMemoryTenantToolPolicyStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public ValueTask<TenantToolPolicy?> GetAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        _policies.TryGetValue(AmbientTenantScope.Normalize(tenantId), out var policy);
        return ValueTask.FromResult(policy);
    }

    /// <inheritdoc />
    public ValueTask<TenantToolPolicy> UpsertAsync(
        string tenantId,
        IReadOnlyList<string> allowedTools,
        IReadOnlyList<string> grantedPermissions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(allowedTools);
        ArgumentNullException.ThrowIfNull(grantedPermissions);
        cancellationToken.ThrowIfCancellationRequested();

        var canonical = AmbientTenantScope.Normalize(tenantId);

        // A copy: the caller may keep a list it later changes, and a policy that changes under the gate
        // would change what a tenant may run without a save.
        var policy = new TenantToolPolicy
        {
            TenantId = canonical,
            AllowedTools = [.. allowedTools],
            GrantedPermissions = [.. grantedPermissions],
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
