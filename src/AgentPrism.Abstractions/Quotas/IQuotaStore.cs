namespace AgentPrism;

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
}
