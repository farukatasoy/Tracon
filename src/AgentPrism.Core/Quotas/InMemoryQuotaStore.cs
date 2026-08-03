using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// Kota kurallarini ve sayaclarini surec belleginde tutan varsayilan uygulama.
/// </summary>
/// <remarks>
/// <para>
/// Tek surecli kurulumlar ve testler icindir. Cok ornekli bir dagitimda
/// <c>UsePostgreSql()</c> bunu <c>PostgresQuotaStore</c> ile degistirir; aksi
/// halde her ornek kendi sayacini tutar ve kota ornege bolunur.
/// </para>
/// <para>
/// Sayac artirma <see cref="ConcurrentDictionary{TKey, TValue}.AddOrUpdate(TKey, Func{TKey, TValue}, Func{TKey, TValue, TValue})"/>
/// ile atomiktir; PostgreSQL uygulamasindaki <c>ON CONFLICT DO UPDATE</c> ile
/// ayni sozlesmeyi saglar ve hicbir artis kaybolmaz.
/// </para>
/// </remarks>
public sealed class InMemoryQuotaStore : IQuotaStore
{
    private readonly ConcurrentDictionary<Guid, QuotaDefinition> _definitions = new();
    private readonly ConcurrentDictionary<UsageKey, QuotaUsageRecord> _usage = new();

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<QuotaDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        IReadOnlyList<QuotaDefinition> result = _definitions.Values
            .Where(definition => string.Equals(definition.TenantId, tenantId, StringComparison.Ordinal))
            .OrderBy(definition => definition.AgentName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(definition => definition.Period)
            .ToList();

        return new ValueTask<IReadOnlyList<QuotaDefinition>>(result);
    }

    /// <inheritdoc />
    public ValueTask<QuotaDefinition?> GetAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var found = _definitions.TryGetValue(id, out var definition)
                    && string.Equals(definition.TenantId, tenantId, StringComparison.Ordinal)
            ? definition
            : null;

        return new ValueTask<QuotaDefinition?>(found);
    }

    /// <inheritdoc />
    public ValueTask<QuotaDefinition> SaveAsync(
        QuotaDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // Kapsam benzersizligi: ayni (kiraci, agent, donem) ucusu icin var olan
        // kural varsa kimligi korunur ve uzerine yazilir. PostgreSQL tarafinda
        // ayni kural COALESCE'li benzersiz indeksle zorlanir.
        var existing = _definitions.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.TenantId, definition.TenantId, StringComparison.Ordinal)
            && string.Equals(candidate.AgentName ?? string.Empty, definition.AgentName ?? string.Empty, StringComparison.Ordinal)
            && candidate.Period == definition.Period);

        if (existing is not null && existing.Id != definition.Id)
        {
            _definitions.TryRemove(existing.Id, out _);
        }

        var saved = definition with { Id = existing?.Id ?? definition.Id };
        _definitions[saved.Id] = saved;

        return new ValueTask<QuotaDefinition>(saved);
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (!_definitions.TryGetValue(id, out var definition)
            || !string.Equals(definition.TenantId, tenantId, StringComparison.Ordinal))
        {
            return new ValueTask<bool>(false);
        }

        return new ValueTask<bool>(_definitions.TryRemove(id, out _));
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<QuotaUsageRecord>> GetUsageAsync(
        QuotaUsageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var matches = _usage.Values
            .Where(record => string.Equals(record.TenantId, query.TenantId, StringComparison.Ordinal));

        if (query.AgentName is { } agentName)
        {
            matches = matches.Where(record =>
                string.Equals(record.AgentName, agentName, StringComparison.Ordinal));
        }

        if (query.Period is { } period)
        {
            matches = matches.Where(record => record.Period == period);
        }

        IReadOnlyList<QuotaUsageRecord> result = matches
            .OrderBy(record => record.AgentName, StringComparer.Ordinal)
            .ThenBy(record => record.Period)
            .ThenByDescending(record => record.PeriodStart)
            .ToList();

        return new ValueTask<IReadOnlyList<QuotaUsageRecord>>(result);
    }

    /// <inheritdoc />
    public ValueTask AddUsageAsync(
        QuotaConsumption consumption,
        IReadOnlyDictionary<QuotaPeriod, DateOnly> periodStarts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consumption);
        ArgumentNullException.ThrowIfNull(periodStarts);

        foreach (var (period, periodStart) in periodStarts)
        {
            // Bir calistirma HEM agent sayacini HEM kiraci geneli sayacini
            // artirir; kiraci geneli kural agent adini bilmeden sorgulanabilsin.
            Increment(consumption, period, periodStart, consumption.AgentName);
            Increment(consumption, period, periodStart, string.Empty);
        }

        return default;
    }

    private void Increment(QuotaConsumption consumption, QuotaPeriod period, DateOnly periodStart, string agentName)
    {
        var key = new UsageKey(consumption.TenantId, agentName, period, periodStart);

        _usage.AddOrUpdate(
            key,
            _ => new QuotaUsageRecord
            {
                TenantId = consumption.TenantId,
                AgentName = agentName,
                Period = period,
                PeriodStart = periodStart,
                Runs = consumption.Runs,
                Tokens = consumption.Tokens,
                // Fiyat tanimsizsa toplama katilmaz; sifir olarak da eklenmez.
                Cost = consumption.Cost ?? 0m,
                UpdatedAt = consumption.OccurredAt,
            },
            (_, current) => current with
            {
                Runs = current.Runs + consumption.Runs,
                Tokens = current.Tokens + consumption.Tokens,
                Cost = current.Cost + (consumption.Cost ?? 0m),
                UpdatedAt = consumption.OccurredAt,
            });
    }

    private readonly record struct UsageKey(string TenantId, string AgentName, QuotaPeriod Period, DateOnly PeriodStart);
}
