using System.Collections.Concurrent;

namespace Tracon;

/// <summary>
/// A store that keeps persistent approval rules in process memory.
/// </summary>
/// <remarks>
/// <strong>Limits:</strong> process lifetime and a single node. When the application
/// restarts, "do not ask again" rules are lost and approval is requested again.
/// This is the safe behavior. Use <c>Tracon.PostgreSql</c> in production.
/// </remarks>
internal sealed class InMemoryToolApprovalRuleStore : IToolApprovalRuleStore
{
    private readonly ConcurrentDictionary<Guid, ToolApprovalRule> _rules = new();

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ToolApprovalRule>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        var matches = _rules.Values
            .Where(rule => string.Equals(rule.TenantId, tenantId, StringComparison.Ordinal))
            .OrderByDescending(static rule => rule.CreatedAt)
            .ToList();

        return new ValueTask<IReadOnlyList<ToolApprovalRule>>(matches);
    }

    /// <inheritdoc />
    public ValueTask<ToolApprovalRule> AddAsync(
        ToolApprovalRule rule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var existing in _rules.Values)
        {
            if (Matches(existing, rule))
            {
                return new ValueTask<ToolApprovalRule>(existing);
            }
        }

        _rules[rule.Id] = rule;

        return new ValueTask<ToolApprovalRule>(rule);
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(
        string tenantId,
        Guid ruleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        // Apply tenant validation to deletion too. An identifier guess must not let
        // a tenant delete another tenant's rule.
        if (_rules.TryGetValue(ruleId, out var rule)
            && string.Equals(rule.TenantId, tenantId, StringComparison.Ordinal))
        {
            return new ValueTask<bool>(_rules.TryRemove(ruleId, out _));
        }

        return new ValueTask<bool>(false);
    }

    private static bool Matches(ToolApprovalRule left, ToolApprovalRule right)
        => string.Equals(left.TenantId, right.TenantId, StringComparison.Ordinal)
            && string.Equals(left.AgentName, right.AgentName, StringComparison.Ordinal)
            && string.Equals(left.ToolName, right.ToolName, StringComparison.Ordinal)
            && string.Equals(left.ArgumentsHash, right.ArgumentsHash, StringComparison.Ordinal)
            && string.Equals(
                ToolArgumentConditionFingerprint.Compute(left.ArgumentConditions),
                ToolArgumentConditionFingerprint.Compute(right.ArgumentConditions),
                StringComparison.Ordinal);
}

/// <summary>
/// A store that keeps MCP server definitions in process memory.
/// </summary>
/// <remarks>Use <c>Tracon.PostgreSql</c> in production.</remarks>
internal sealed class InMemoryMcpServerStore : IMcpServerStore
{
    // A tuple, not a joined string: a joined key needs a separator that no
    // tenant id or name can contain, and nothing at this layer enforces one.
    private readonly ConcurrentDictionary<(string TenantId, string Name), McpServerDefinition> _servers = new();

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<McpServerDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        var matches = _servers.Values
            .Where(server => string.Equals(server.TenantId, tenantId, StringComparison.Ordinal))
            .OrderBy(static server => server.Name, StringComparer.Ordinal)
            .ToList();

        return new ValueTask<IReadOnlyList<McpServerDefinition>>(matches);
    }

    /// <inheritdoc />
    public ValueTask<McpServerDefinition?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        _servers.TryGetValue(Key(tenantId, name), out var server);

        return new ValueTask<McpServerDefinition?>(server);
    }

    /// <inheritdoc />
    public ValueTask<McpServerDefinition> SaveAsync(
        McpServerDefinition server,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        cancellationToken.ThrowIfCancellationRequested();

        var key = Key(server.TenantId, server.Name);
        var now = DateTimeOffset.UtcNow;

        var saved = _servers.TryGetValue(key, out var existing)
            ? server with { Id = existing.Id, CreatedAt = existing.CreatedAt, UpdatedAt = now }
            : server with
            {
                Id = server.Id == Guid.Empty ? TraconId.NewId() : server.Id,
                CreatedAt = now,
                UpdatedAt = now,
            };

        _servers[key] = saved;

        return new ValueTask<McpServerDefinition>(saved);
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        return new ValueTask<bool>(_servers.TryRemove(Key(tenantId, name), out _));
    }

    private static (string TenantId, string Name) Key(string tenantId, string name) => (tenantId, name);
}

/// <summary>
/// A store that keeps tenant records in process memory.
/// </summary>
/// <remarks>Use <c>Tracon.PostgreSql</c> in production.</remarks>
internal sealed class InMemoryTenantStore : ITenantStore
{
    private readonly ConcurrentDictionary<string, TenantDescriptor> _tenants = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<TenantDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new(_tenants.Values
            .OrderBy(static tenant => tenant.Slug, StringComparer.Ordinal)
            .ToList());
    }

    /// <inheritdoc />
    public ValueTask<TenantDescriptor> SaveAsync(
        TenantDescriptor tenant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        cancellationToken.ThrowIfCancellationRequested();

        // The slug IS the tenant identifier, so it is folded the same way every
        // other table folds tenant_id. A record saved as 'Acme' would
        // otherwise name a tenant the runtime never resolves.
        var slug = AmbientTenantScope.Normalize(tenant.Slug);

        var saved = _tenants.TryGetValue(slug, out var existing)
            ? tenant with { Slug = slug, Id = existing.Id, CreatedAt = existing.CreatedAt }
            : tenant with
            {
                Slug = slug,
                Id = tenant.Id == Guid.Empty ? TraconId.NewId() : tenant.Id,
                CreatedAt = DateTimeOffset.UtcNow,
            };

        _tenants[saved.Slug] = saved;

        return new ValueTask<TenantDescriptor>(saved);
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(string slug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        cancellationToken.ThrowIfCancellationRequested();

        return new ValueTask<bool>(_tenants.TryRemove(AmbientTenantScope.Normalize(slug), out _));
    }
}
