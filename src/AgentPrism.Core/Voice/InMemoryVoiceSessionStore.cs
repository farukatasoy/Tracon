using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// Konusma kayitlarini surec belleginde tutan varsayilan uygulama.
/// </summary>
/// <remarks>
/// <para>
/// Tek surecli kurulumlar ve testler icindir. Bir SQL saglayicisi acildiginda
/// (<c>UsePostgreSql()</c>, <c>UseSqlServer()</c>, <c>UseSqlite()</c>) bunun
/// yerini kalici depo alir.
/// </para>
/// <para>
/// Kayit sayisi ust sinirlidir: konusma baglantisi acilip kapandikca liste
/// sinirsiz buyurdu ve bellek ici bir kurulumu sessizce sisirirdi.
/// </para>
/// </remarks>
public sealed class InMemoryVoiceSessionStore : IVoiceSessionStore
{
    /// <summary>Bellekte tutulacak en fazla kayit.</summary>
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
