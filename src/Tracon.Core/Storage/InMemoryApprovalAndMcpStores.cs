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
            && ConditionsEqual(left.ArgumentConditions, right.ArgumentConditions);

    /// <summary>
    /// Order-independent condition-set equality, mirroring the SQL stores'
    /// <c>conditions_hash</c> uniqueness key (canonical order, exact JSON text).
    /// </summary>
    private static bool ConditionsEqual(IReadOnlyList<ToolArgumentCondition> left, IReadOnlyList<ToolArgumentCondition> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        if (left.Count == 0)
        {
            return true;
        }

        var leftSorted = left.OrderBy(static c => c.Path, StringComparer.Ordinal)
            .ThenBy(static c => (int)c.Operator)
            .ThenBy(static c => c.Value.GetRawText(), StringComparer.Ordinal)
            .ToList();
        var rightSorted = right.OrderBy(static c => c.Path, StringComparer.Ordinal)
            .ThenBy(static c => (int)c.Operator)
            .ThenBy(static c => c.Value.GetRawText(), StringComparer.Ordinal)
            .ToList();

        for (var i = 0; i < leftSorted.Count; i++)
        {
            if (!string.Equals(leftSorted[i].Path, rightSorted[i].Path, StringComparison.Ordinal)
                || leftSorted[i].Operator != rightSorted[i].Operator
                || !string.Equals(leftSorted[i].Value.GetRawText(), rightSorted[i].Value.GetRawText(), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// A store that keeps MCP server definitions in process memory.
/// </summary>
/// <remarks>Use <c>Tracon.PostgreSql</c> in production.</remarks>
internal sealed class InMemoryMcpServerStore : IMcpServerStore
{
    private readonly ConcurrentDictionary<string, McpServerDefinition> _servers = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<McpServerDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

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

        _servers.TryGetValue(Key(tenantId, name), out var server);

        return new ValueTask<McpServerDefinition?>(server);
    }

    /// <inheritdoc />
    public ValueTask<McpServerDefinition> SaveAsync(
        McpServerDefinition server,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);

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

        return new ValueTask<bool>(_servers.TryRemove(Key(tenantId, name), out _));
    }

    private static string Key(string tenantId, string name) => $"{tenantId}{name}";
}

/// <summary>
/// A store that keeps tenant records in process memory.
/// </summary>
/// <remarks>Uretimde <c>Tracon.PostgreSql</c> kullanin.</remarks>
internal sealed class InMemoryTenantStore : ITenantStore
{
    private readonly ConcurrentDictionary<string, TenantDescriptor> _tenants = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<TenantDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        => new(_tenants.Values
            .OrderBy(static tenant => tenant.Slug, StringComparer.Ordinal)
            .ToList());

    /// <inheritdoc />
    public ValueTask<TenantDescriptor> SaveAsync(
        TenantDescriptor tenant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var saved = _tenants.TryGetValue(tenant.Slug, out var existing)
            ? tenant with { Id = existing.Id, CreatedAt = existing.CreatedAt }
            : tenant with
            {
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

        return new ValueTask<bool>(_tenants.TryRemove(slug, out _));
    }
}
