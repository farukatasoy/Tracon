using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// Oturumlari surec bellegi icinde tutan depo.
/// </summary>
/// <remarks>
/// <para>
/// Sinirlari <see cref="InMemoryAgentDefinitionStore"/> ile aynidir: surec omru
/// ve tek dugum. Uretimde <c>AgentPrism.PostgreSql</c> kullanin.
/// </para>
/// <para>
/// 🚨 Oturumlar <strong>kiraci basina</strong> ayrilir; kiraci
/// <see cref="ITenantContext"/>'ten okunur (Faz 41).
/// </para>
/// </remarks>
public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<(string TenantId, string Id), SessionRecord> _sessions = new();
    private readonly ITenantContext _tenantContext;

    /// <summary>Yeni bir bellek ici oturum deposu olusturur.</summary>
    /// <param name="tenantContext">
    /// Gecerli kiracinin baglami. Verilmezse depo tek kiracili davranir.
    /// </param>
    public InMemorySessionStore(ITenantContext? tenantContext = null)
        => _tenantContext = tenantContext ?? FixedTenantContext.Default;

    /// <inheritdoc />
    /// <remarks>
    /// Ayni kimlikle kayit varsa <see cref="SessionRecord.CreatedAt"/> korunur.
    /// "Olusturulma zamani" ilk yazmaya aittir; kalici depo da ayni davranisi gosterir.
    /// </remarks>
    public ValueTask SaveAsync(SessionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        // SQL uygulamasiyla ayni kural: kayit kendi kiracisini tasiyorsa o
        // kazanir (zamanlanmis isler baska bir kiraci adina yazabilir).
        var tenantId = record.TenantId ?? _tenantContext.TenantId;

        _sessions.AddOrUpdate(
            (tenantId, record.Id),
            static (_, incoming) => incoming,
            static (_, existing, incoming) => incoming with { CreatedAt = existing.CreatedAt },
            record with { TenantId = tenantId });

        return default;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="ConcurrentDictionary{TKey, TValue}.TryAdd(TKey, TValue)"/> atomiktir:
    /// ayni kimlikle eszamanli iki cagridan yalniz biri <see langword="true"/> doner
    /// (HATA-004).
    /// </remarks>
    public ValueTask<bool> TryCreateAsync(SessionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var tenantId = record.TenantId ?? _tenantContext.TenantId;

        return new ValueTask<bool>(_sessions.TryAdd((tenantId, record.Id), record with { TenantId = tenantId }));
    }

    /// <inheritdoc />
    public ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        _sessions.TryGetValue((_tenantContext.TenantId, sessionId), out var record);
        return new ValueTask<SessionRecord?>(record);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Anahtar <c>(TenantId, Id)</c> bilesigi oldugundan ambient kiraciyle
    /// filtrelenemez; kimligi tasiyan kaydi butun kiracilar arasinda tarar.
    /// </remarks>
    public ValueTask<string?> GetOwnerTenantIdAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        foreach (var key in _sessions.Keys)
        {
            if (string.Equals(key.Id, sessionId, StringComparison.Ordinal))
            {
                return new ValueTask<string?>(key.TenantId);
            }
        }

        return new ValueTask<string?>((string?)null);
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        return new ValueTask<bool>(_sessions.TryRemove((_tenantContext.TenantId, sessionId), out _));
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<SessionRecord>> QueryAsync(SessionQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var matches = new List<SessionRecord>();
        var tenantId = query.TenantId ?? _tenantContext.TenantId;

        foreach (var record in _sessions.Values)
        {
            if (query.AgentName is { } agentName && !string.Equals(record.AgentName, agentName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(record.TenantId, tenantId, StringComparison.Ordinal))
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
