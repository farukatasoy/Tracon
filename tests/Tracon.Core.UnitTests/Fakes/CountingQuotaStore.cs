using System.Collections.Concurrent;

namespace Tracon.Core.UnitTests.Fakes;

/// <summary>
/// Counts the list and usage calls a quota store receives, records every usage
/// query, and can hold a usage call until the test releases it.
/// </summary>
internal sealed class CountingQuotaStore(IQuotaStore inner) : IQuotaStore
{
    private int _listCalls;
    private int _usageCalls;

    public int ListCalls => Volatile.Read(ref _listCalls);

    public int UsageCalls => Volatile.Read(ref _usageCalls);

    public TaskCompletionSource? Stall { get; init; }

    /// <summary>Every usage query, in call order.</summary>
    public ConcurrentQueue<QuotaUsageQuery> UsageQueries { get; } = new();

    public ValueTask<IReadOnlyList<QuotaDefinition>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _listCalls);

        return inner.ListAsync(tenantId, cancellationToken);
    }

    public ValueTask<QuotaDefinition?> GetAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
        => inner.GetAsync(tenantId, id, cancellationToken);

    public ValueTask<QuotaDefinition> SaveAsync(QuotaDefinition definition, CancellationToken cancellationToken = default)
        => inner.SaveAsync(definition, cancellationToken);

    public ValueTask<bool> DeleteAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
        => inner.DeleteAsync(tenantId, id, cancellationToken);

    public ValueTask<IReadOnlyList<QuotaUsageRecord>> GetUsageAsync(QuotaUsageQuery query, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _usageCalls);
        UsageQueries.Enqueue(query);

        return Stall is { } stall
            ? StalledUsageAsync(stall, query, cancellationToken)
            : inner.GetUsageAsync(query, cancellationToken);
    }

    public ValueTask AddUsageAsync(
        QuotaConsumption consumption,
        IReadOnlyDictionary<QuotaPeriod, DateOnly> periodStarts,
        CancellationToken cancellationToken = default)
        => inner.AddUsageAsync(consumption, periodStarts, cancellationToken);

    public ValueTask<bool> TryClaimThresholdNotificationAsync(
        string tenantId,
        string agentName,
        QuotaPeriod period,
        DateOnly periodStart,
        QuotaMetric metric,
        int thresholdPercent,
        CancellationToken cancellationToken = default)
        => inner.TryClaimThresholdNotificationAsync(tenantId, agentName, period, periodStart, metric, thresholdPercent, cancellationToken);

    private async ValueTask<IReadOnlyList<QuotaUsageRecord>> StalledUsageAsync(
        TaskCompletionSource stall,
        QuotaUsageQuery query,
        CancellationToken cancellationToken)
    {
        await stall.Task.WaitAsync(cancellationToken);

        return await inner.GetUsageAsync(query, cancellationToken);
    }
}
