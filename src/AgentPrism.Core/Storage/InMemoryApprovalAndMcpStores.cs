using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// Kalici onay kurallarini surec bellegi icinde tutan depo.
/// </summary>
/// <remarks>
/// <strong>Sinirlari:</strong> surec omru ve tek dugum. Uygulama yeniden
/// baslatildiginda "bir daha sorma" kurallari kaybolur ve onay yeniden sorulur.
/// Bu, guvenli taraftaki davranistir. Uretimde <c>AgentPrism.PostgreSql</c> kullanin.
/// </remarks>
public sealed class InMemoryToolApprovalRuleStore : IToolApprovalRuleStore
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

        // Kiraci denetimi kaldirma islemine de uygulanir: baska bir kiracinin
        // kurali kimlik tahminiyle silinememelidir.
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
            && string.Equals(left.ArgumentsHash, right.ArgumentsHash, StringComparison.Ordinal);
}

/// <summary>
/// MCP sunucu tanimlarini surec bellegi icinde tutan depo.
/// </summary>
/// <remarks>Uretimde <c>AgentPrism.PostgreSql</c> kullanin.</remarks>
public sealed class InMemoryMcpServerStore : IMcpServerStore
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
                Id = server.Id == Guid.Empty ? AgentPrismId.NewId() : server.Id,
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
/// Kiraci kayitlarini surec bellegi icinde tutan depo.
/// </summary>
/// <remarks>Uretimde <c>AgentPrism.PostgreSql</c> kullanin.</remarks>
public sealed class InMemoryTenantStore : ITenantStore
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
                Id = tenant.Id == Guid.Empty ? AgentPrismId.NewId() : tenant.Id,
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
