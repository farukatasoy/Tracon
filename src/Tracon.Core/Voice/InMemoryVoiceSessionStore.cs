using System.Collections.Concurrent;

namespace Tracon;

/// <summary>
/// The default implementation that keeps voice records in process memory.
/// </summary>
/// <remarks>
/// <para>
/// For single-process deployments and tests. When a SQL provider is enabled
/// with <c>UsePostgreSql()</c>, <c>UseSqlServer()</c>, or <c>UseSqlite()</c>,
/// a persistent store replaces this one.
/// </para>
/// <para>
/// The record count has an upper limit. As voice connections open and close, an
/// unbounded list would silently grow an in-memory deployment.
/// </para>
/// </remarks>
internal sealed class InMemoryVoiceSessionStore : IVoiceSessionStore
{
    /// <summary>The maximum number of records kept in memory.</summary>
    private const int Capacity = 2_000;

    private readonly ConcurrentDictionary<Guid, VoiceSessionRecord> _records = new();

    /// <inheritdoc />
    public ValueTask SaveAsync(VoiceSessionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        _records[record.Id] = record;

        if (_records.Count > Capacity)
        {
            TrimOldest();
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<VoiceSessionRecord>> QueryAsync(
        string tenantId,
        VoiceSessionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<VoiceSessionRecord> result = _records.Values
            .Where(record => string.Equals(record.TenantId, tenantId, StringComparison.Ordinal))
            .Where(record => query.AgentName is not { Length: > 0 } agent
                             || string.Equals(record.AgentName, agent, StringComparison.Ordinal))
            .Where(record => query.SessionId is not { Length: > 0 } session
                             || string.Equals(record.SessionId, session, StringComparison.Ordinal))
            .OrderByDescending(record => record.StartedAt)
            .Skip(Math.Max(0, query.Skip))
            .Take(Math.Clamp(query.Take, 1, 200))
            .ToList();

        return new ValueTask<IReadOnlyList<VoiceSessionRecord>>(result);
    }

    private void TrimOldest()
    {
        var doomed = _records.Values
            .OrderBy(record => record.StartedAt)
            .Take(_records.Count - Capacity)
            .Select(record => record.Id)
            .ToList();

        foreach (var id in doomed)
        {
            _records.TryRemove(id, out _);
        }
    }
}
