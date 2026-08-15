using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>An audit log that keeps entries in process memory.</summary>
/// <remarks>Use <c>AgentPrism.PostgreSql</c> in production.</remarks>
public sealed class InMemoryAuditLog : IAuditLog
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();

    /// <inheritdoc />
    public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _entries.Enqueue(entry);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IEnumerable<AuditEntry> matches = _entries;

        if (query.TenantId is { Length: > 0 } tenantId)
        {
            matches = matches.Where(entry => string.Equals(entry.TenantId, tenantId, StringComparison.Ordinal));
        }

        if (query.Actor is { Length: > 0 } actor)
        {
            matches = matches.Where(entry => string.Equals(entry.Actor, actor, StringComparison.Ordinal));
        }

        if (query.Action is { Length: > 0 } action)
        {
            matches = matches.Where(entry => string.Equals(entry.Action, action, StringComparison.Ordinal));
        }

        if (query.Entity is { Length: > 0 } entity)
        {
            matches = matches.Where(entry => string.Equals(entry.Entity, entity, StringComparison.Ordinal));
        }

        if (query.After is { } after)
        {
            matches = matches.Where(entry => entry.CreatedAt > after);
        }

        if (query.Before is { } before)
        {
            matches = matches.Where(entry => entry.CreatedAt < before);
        }

        var result = matches
            .OrderByDescending(static entry => entry.CreatedAt)
            .Take(Math.Max(query.Limit, 0))
            .ToList();

        return new ValueTask<IReadOnlyList<AuditEntry>>(result);
    }
}
