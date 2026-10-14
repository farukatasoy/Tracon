namespace Tracon;

/// <summary>
/// Writes one consumption entry into the quota counters. The seam
/// <see cref="TreeAccount"/> carries so that a run which ends after its root,
/// and a tool call that ends after its run, can still write.
/// </summary>
/// <remarks>
/// Internal on purpose: the only implementation is <c>QuotaEnforcer</c>, and a
/// consumer replaces the counters' storage (<see cref="IQuotaStore"/>), not the
/// rule that decides what a run adds to them.
/// </remarks>
internal interface IQuotaRecorder
{
    /// <summary>Adds the consumption to the counters and claims the thresholds it newly crosses.</summary>
    /// <param name="consumption">The consumption to add.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The thresholds this consumption newly crossed; empty when quotas are off or the write failed.</returns>
    ValueTask<IReadOnlyList<QuotaThresholdCrossing>> RecordAsync(
        QuotaConsumption consumption,
        CancellationToken cancellationToken = default);
}
