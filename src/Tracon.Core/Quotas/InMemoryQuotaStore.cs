using System.Collections.Concurrent;
using System.Globalization;

namespace Tracon;

/// <summary>
/// The default implementation that keeps quota rules and counters in process memory.
/// </summary>
/// <remarks>
/// <para>
/// For single-process deployments and tests. In a multi-instance deployment,
/// <c>UsePostgreSql()</c> replaces this with <c>PostgresQuotaStore</c>. Otherwise,
/// each instance keeps its own counter and the quota is split between instances.
/// </para>
/// <para>
/// Sayac artirma <see cref="ConcurrentDictionary{TKey, TValue}.AddOrUpdate(TKey,
/// Func{TKey, TValue}, Func{TKey, TValue, TValue})"/>
/// is atomic. It provides the same contract as <c>ON CONFLICT DO UPDATE</c> in the
/// PostgreSQL implementation, so no increment is lost.
/// </para>
/// </remarks>
internal sealed class InMemoryQuotaStore : IQuotaStore
{
    private readonly ConcurrentDictionary<Guid, QuotaDefinition> _definitions = new();
    private readonly ConcurrentDictionary<UsageKey, QuotaUsageRecord> _usage = new();

    // 🚨 A SEPARATE dictionary, keyed the same way as _usage, not a field on
    // QuotaUsageRecord: the record is replaced wholesale on every increment
    // (AddOrUpdate), and a claim recorded on the OLD instance would be lost
    // the moment the next run's consumption replaces it.
    private readonly ConcurrentDictionary<UsageKey, ConcurrentDictionary<string, byte>> _notifiedThresholds = new();

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<QuotaDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

        // Scope uniqueness: if a rule exists for the same tenant, agent, and period,
        // preserve its identifier and overwrite it. PostgreSQL enforces the same rule
        // with a unique index that uses COALESCE.
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
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

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

        if (query.PeriodStarts is { } periodStarts)
        {
            matches = matches.Where(record =>
                periodStarts.TryGetValue(record.Period, out var periodStart) && record.PeriodStart == periodStart);
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
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var (period, periodStart) in periodStarts)
        {
            // A run increments both the agent counter and tenant-wide counter, so a
            // tenant-wide rule can be queried without knowing the agent name.
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
                // When the price is undefined, it is not included in the sum or added as zero.
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

    /// <inheritdoc />
    /// <remarks>
    /// The claim survives only for the process's lifetime — the same
    /// limitation this whole store has (single-process deployments only, see
    /// the class remarks). Restart safety is what <c>SqlQuotaStore</c> adds.
    /// </remarks>
    public ValueTask<bool> TryClaimThresholdNotificationAsync(
        string tenantId,
        string agentName,
        QuotaPeriod period,
        DateOnly periodStart,
        QuotaMetric metric,
        int thresholdPercent,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(agentName);
        cancellationToken.ThrowIfCancellationRequested();

        var usageKey = new UsageKey(tenantId, agentName, period, periodStart);

        if (!_usage.ContainsKey(usageKey))
        {
            return new ValueTask<bool>(false);
        }

        var claimed = _notifiedThresholds
            .GetOrAdd(usageKey, static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal))
            .TryAdd(ThresholdKeyText(metric, thresholdPercent), 0);

        return new ValueTask<bool>(claimed);
    }

    private static string ThresholdKeyText(QuotaMetric metric, int thresholdPercent)
        => string.Create(CultureInfo.InvariantCulture, $"{(int)metric}:{thresholdPercent}");

    private readonly record struct UsageKey(string TenantId, string AgentName, QuotaPeriod Period, DateOnly PeriodStart);
}
