namespace Tracon;

/// <summary>The store for quota rules and consumption counters.</summary>
/// <remarks>
/// <para>
/// Two different kinds of data live here: <em>rules</em> (defined by an
/// administrator, change rarely) and <em>counters</em> (increment at the end
/// of every run). Both sit in the same contract because a check always reads
/// them together.
/// </para>
/// <para>
/// <strong>Important:</strong> <see cref="AddUsageAsync"/> must be atomic —
/// the PostgreSQL implementation uses <c>INSERT ... ON CONFLICT DO UPDATE</c>.
/// Concurrent runs increment the same row and no increment is lost.
/// </para>
/// </remarks>
public interface IQuotaStore
{
    /// <summary>Lists a tenant's quota rules.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rules.</returns>
    ValueTask<IReadOnlyList<QuotaDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches a single rule.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="id">The rule identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rule; <see langword="null"/> if it does not exist or belongs to another tenant.</returns>
    ValueTask<QuotaDefinition?> GetAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a rule. Overwrites an existing rule for the same scope (tenant +
    /// agent + period), if one already exists.
    /// </summary>
    /// <param name="definition">The rule.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The saved rule.</returns>
    /// <remarks>
    /// Scope uniqueness is built over <c>COALESCE(agent_name, '')</c>; a plain
    /// <c>UNIQUE</c> constraint treats NULLs as distinct from each other and
    /// would let the same rule be added an unlimited number of times.
    /// </remarks>
    ValueTask<QuotaDefinition> SaveAsync(
        QuotaDefinition definition,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a rule.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="id">The rule identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the delete happened.</returns>
    ValueTask<bool> DeleteAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches the current period's counters.</summary>
    /// <param name="query">The filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The counters. An empty list when there is no consumption at all.</returns>
    ValueTask<IReadOnlyList<QuotaUsageRecord>> GetUsageAsync(
        QuotaUsageQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a consumption to both the agent counter and the tenant-wide counter.
    /// </summary>
    /// <param name="consumption">The consumption.</param>
    /// <param name="periodStarts">
    /// For each period, the first day of the period the consumption falls
    /// into. The caller computes this from the configured time zone; the
    /// store does not know time zones.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// The store <strong>carries no</strong> time zone: the caller computes
    /// the period boundary and passes it ready-made. This lets the same store
    /// behave correctly for consumers running in different time zones.
    /// </remarks>
    ValueTask AddUsageAsync(
        QuotaConsumption consumption,
        IReadOnlyDictionary<QuotaPeriod, DateOnly> periodStarts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims a quota threshold notification for one scope/period,
    /// so that only one caller among concurrent workers — and only the first
    /// run to reach it across a process restart — proceeds to publish it.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="agentName">
    /// The scope: an agent name, or an empty string (<c>""</c>) for the
    /// tenant-wide counter — the same convention <see cref="QuotaUsageRecord.AgentName"/> uses.
    /// </param>
    /// <param name="period">The counter's interval.</param>
    /// <param name="periodStart">The first day of the period (local time).</param>
    /// <param name="metric">The metric whose threshold was crossed.</param>
    /// <param name="thresholdPercent">The crossed threshold percentage.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if this call is the one that claims the
    /// threshold — no earlier call claimed it in this period, and the caller
    /// should publish the notice. <see langword="false"/> if it was already
    /// claimed (by an earlier run, or a concurrent worker), or the usage row
    /// for this scope/period does not exist yet.
    /// </returns>
    /// <remarks>
    /// The claim survives a process restart: it lives in the same durable
    /// row <see cref="AddUsageAsync"/> writes to, not in memory. When the
    /// period rolls over, a new row forms and the claim starts clean —
    /// there is nothing to reset by hand.
    /// </remarks>
    ValueTask<bool> TryClaimThresholdNotificationAsync(
        string tenantId,
        string agentName,
        QuotaPeriod period,
        DateOnly periodStart,
        QuotaMetric metric,
        int thresholdPercent,
        CancellationToken cancellationToken = default);
}
