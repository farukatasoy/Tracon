using System.Collections.Concurrent;

namespace Tracon;

/// <summary>A store that keeps inbound trigger definitions in process memory (first class).</summary>
/// <remarks>
/// <strong>Limits:</strong> process lifetime and a single node. Use a SQL
/// provider in production. Its behavior contract matches
/// <c>SqlInboundTriggerStore</c> exactly and shared contract tests protect it.
/// </remarks>
internal sealed class InMemoryInboundTriggerStore : IInboundTriggerStore
{
    private readonly ConcurrentDictionary<Guid, InboundTrigger> _triggers = new();

    /// <inheritdoc />
    public ValueTask<InboundTrigger?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new ValueTask<InboundTrigger?>(Find(tenantId, name));
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<InboundTrigger>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var matches = _triggers.Values
            .Where(trigger => string.Equals(trigger.TenantId, tenantId, StringComparison.Ordinal))
            .OrderBy(static trigger => trigger.Name, StringComparer.Ordinal)
            .ToList();

        return new ValueTask<IReadOnlyList<InboundTrigger>>(matches);
    }

    /// <inheritdoc />
    public ValueTask<InboundTrigger> UpsertAsync(InboundTrigger trigger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        lock (_triggers)
        {
            var existing = Find(trigger.TenantId, trigger.Name);

            var saved = trigger with
            {
                Id = existing?.Id ?? (trigger.Id == Guid.Empty ? Guid.NewGuid() : trigger.Id),
                CreatedAt = existing?.CreatedAt ?? trigger.CreatedAt,
            };

            _triggers[saved.Id] = saved;

            return new ValueTask<InboundTrigger>(saved);
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_triggers)
        {
            var match = Find(tenantId, name);

            return new ValueTask<bool>(match is not null && _triggers.TryRemove(match.Id, out _));
        }
    }

    private InboundTrigger? Find(string tenantId, string name)
        => _triggers.Values.FirstOrDefault(trigger
            => string.Equals(trigger.TenantId, tenantId, StringComparison.Ordinal)
            && string.Equals(trigger.Name, name, StringComparison.Ordinal));
}
