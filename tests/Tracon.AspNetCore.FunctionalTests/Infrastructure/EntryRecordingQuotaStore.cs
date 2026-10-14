namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// The in-memory quota store with every entry written to it kept, and an optional
/// predicate that makes a write fail: the way a test sees what a run put into the
/// counters, one entry at a time, and what happens when the counter cannot be written.
/// </summary>
internal sealed class EntryRecordingQuotaStore : IQuotaStore
{
    private readonly InMemoryQuotaStore _inner = new();
    private readonly List<QuotaConsumption> _entries = [];

    /// <summary>Makes <see cref="AddUsageAsync"/> throw for every entry the predicate accepts.</summary>
    public Func<QuotaConsumption, bool>? FailWhen { get; set; }

    /// <summary>The entries written so far, in order, including the ones that failed.</summary>
    public IReadOnlyList<QuotaConsumption> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public ValueTask<IReadOnlyList<QuotaDefinition>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
        => _inner.ListAsync(tenantId, cancellationToken);

    public ValueTask<QuotaDefinition?> GetAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
        => _inner.GetAsync(tenantId, id, cancellationToken);

    public ValueTask<QuotaDefinition> SaveAsync(QuotaDefinition definition, CancellationToken cancellationToken = default)
        => _inner.SaveAsync(definition, cancellationToken);

    public ValueTask<bool> DeleteAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
        => _inner.DeleteAsync(tenantId, id, cancellationToken);

    public ValueTask<IReadOnlyList<QuotaUsageRecord>> GetUsageAsync(QuotaUsageQuery query, CancellationToken cancellationToken = default)
        => _inner.GetUsageAsync(query, cancellationToken);

    public ValueTask AddUsageAsync(
        QuotaConsumption consumption,
        IReadOnlyDictionary<QuotaPeriod, DateOnly> periodStarts,
        CancellationToken cancellationToken = default)
    {
        lock (_entries)
        {
            _entries.Add(consumption);
        }

        if (FailWhen?.Invoke(consumption) == true)
        {
            throw new InvalidOperationException("The quota store cannot be written.");
        }

        return _inner.AddUsageAsync(consumption, periodStarts, cancellationToken);
    }

    public ValueTask<bool> TryClaimThresholdNotificationAsync(
        string tenantId,
        string agentName,
        QuotaPeriod period,
        DateOnly periodStart,
        QuotaMetric metric,
        int thresholdPercent,
        CancellationToken cancellationToken = default)
        => _inner.TryClaimThresholdNotificationAsync(
            tenantId, agentName, period, periodStart, metric, thresholdPercent, cancellationToken);
}
