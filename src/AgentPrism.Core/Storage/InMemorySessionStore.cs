using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// Oturumlari surec bellegi icinde tutan depo.
/// </summary>
/// <remarks>
/// Sinirlari <see cref="InMemoryAgentDefinitionStore"/> ile aynidir: surec omru
/// ve tek dugum. Uretimde <c>AgentPrism.PostgreSql</c> kullanin.
/// </remarks>
public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, SessionRecord> _sessions = new(StringComparer.Ordinal);

    /// <inheritdoc />
    /// <remarks>
    /// Ayni kimlikle kayit varsa <see cref="SessionRecord.CreatedAt"/> korunur.
    /// "Olusturulma zamani" ilk yazmaya aittir; kalici depo da ayni davranisi gosterir.
    /// </remarks>
    public ValueTask SaveAsync(SessionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        _sessions.AddOrUpdate(
            record.Id,
            static (_, incoming) => incoming,
            static (_, existing, incoming) => incoming with { CreatedAt = existing.CreatedAt },
            record);

        return default;
    }

    /// <inheritdoc />
    public ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        _sessions.TryGetValue(sessionId, out var record);
        return new ValueTask<SessionRecord?>(record);
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        return new ValueTask<bool>(_sessions.TryRemove(sessionId, out _));
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<SessionRecord>> QueryAsync(SessionQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var matches = new List<SessionRecord>();

        foreach (var record in _sessions.Values)
        {
            if (query.AgentName is { } agentName && !string.Equals(record.AgentName, agentName, StringComparison.Ordinal))
            {
                continue;
            }

            if (query.TenantId is { } tenantId && !string.Equals(record.TenantId, tenantId, StringComparison.Ordinal))
            {
                continue;
            }

            matches.Add(record);
        }

        matches.Sort(static (left, right) => right.UpdatedAt.CompareTo(left.UpdatedAt));

        return new ValueTask<IReadOnlyList<SessionRecord>>(Paginate(matches, query.Skip, query.Take));
    }

    private static List<T> Paginate<T>(List<T> source, int skip, int take)
    {
        if (skip <= 0 && take >= source.Count)
        {
            return source;
        }

        var start = Math.Clamp(skip, 0, source.Count);
        var count = Math.Clamp(take, 0, source.Count - start);

        return source.GetRange(start, count);
    }
}
