using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>The metric a quota is exceeded by.</summary>
/// <remarks>
/// Written as a name in JSON. The value order <strong>must not change</strong>
/// — only append.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<QuotaMetric>))]
public enum QuotaMetric
{
    /// <summary>The number of runs.</summary>
    Runs = 0,

    /// <summary>The total tokens (input + output).</summary>
    Tokens = 1,

    /// <summary>The total monetary amount.</summary>
    Cost = 2,
}

/// <summary>A quota rule defined for a tenant or an agent.</summary>
/// <remarks>
/// <para>
/// A quota is at <strong>day/month</strong> scale and is counted in the
/// database. The <strong>rate limit</strong> that smooths bursty load is a
/// separate mechanism and lives in memory; the two must not be confused.
/// </para>
/// <para>
/// All three limits may be <see langword="null"/>: only the ones that are set
/// are enforced. If all three are empty, the rule does nothing.
/// </para>
/// </remarks>
public sealed record QuotaDefinition
{
    /// <summary>The rule identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>The tenant the rule belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// The agent the rule is bound to. If <see langword="null"/>, the rule
    /// applies to all of the tenant's runs.
    /// </summary>
    public string? AgentName { get; init; }

    /// <summary>The counter's reset interval.</summary>
    public required QuotaPeriod Period { get; init; }

    /// <summary>The maximum number of runs per period. Unlimited if <see langword="null"/>.</summary>
    public long? MaxRuns { get; init; }

    /// <summary>The maximum tokens per period. Unlimited if <see langword="null"/>.</summary>
    public long? MaxTokens { get; init; }

    /// <summary>
    /// The maximum monetary amount per period. Unlimited if <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// This limit <strong>cannot be enforced</strong> on a model with undefined
    /// pricing: since the cost is unknown, the quota falls back to tokens. See
    /// <see cref="PricingSource.Unknown"/>.
    /// </remarks>
    public decimal? MaxCost { get; init; }

    /// <summary>Whether the rule is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The creation time (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The last-updated time (UTC).</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>A scope's consumption in the current period.</summary>
public sealed record QuotaUsageRecord
{
    /// <summary>The tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// The agent name. An empty string (<c>""</c>) means the tenant-wide counter.
    /// </summary>
    public required string AgentName { get; init; }

    /// <summary>The counter's interval.</summary>
    public required QuotaPeriod Period { get; init; }

    /// <summary>The first day of the period (in local time).</summary>
    public required DateOnly PeriodStart { get; init; }

    /// <summary>The number of runs completed in the period.</summary>
    public long Runs { get; init; }

    /// <summary>The total tokens spent in the period.</summary>
    public long Tokens { get; init; }

    /// <summary>
    /// The total amount spent in the period. Runs with undefined pricing are
    /// <strong>not included</strong> in this total (not even added as zero).
    /// </summary>
    public decimal Cost { get; init; }

    /// <summary>The last-updated time (UTC).</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>The result of a quota check.</summary>
/// <remarks>
/// The check happens <strong>before the run starts</strong>; consumption is
/// written after the run ends. This means concurrent runs can push a quota
/// slightly over — the quota is <strong>approximate</strong>.
/// </remarks>
public sealed record QuotaDecision
{
    /// <summary>The result reporting that no rule was exceeded.</summary>
    public static QuotaDecision Allowed { get; } = new() { IsAllowed = true };

    /// <summary>Whether the run is allowed.</summary>
    public required bool IsAllowed { get; init; }

    /// <summary>The exceeded metric. <see langword="null"/> if allowed.</summary>
    public QuotaMetric? Metric { get; init; }

    /// <summary>The exceeded rule's scope: the agent name, or <see langword="null"/> for tenant-wide.</summary>
    public string? AgentName { get; init; }

    /// <summary>The exceeded rule's period. <see langword="null"/> if allowed.</summary>
    public QuotaPeriod? Period { get; init; }

    /// <summary>The exceeded limit value.</summary>
    public decimal? Limit { get; init; }

    /// <summary>The consumption at check time.</summary>
    public decimal? Used { get; init; }

    /// <summary>The time the counter resets (UTC). Reflected to the client as <c>Retry-After</c>.</summary>
    public DateTimeOffset? ResetsAt { get; init; }

    /// <summary>
    /// <see langword="true"/> if a monetary rule fell back to tokens because
    /// pricing was undefined.
    /// </summary>
    public bool CostFellBackToTokens { get; init; }

    /// <summary>The explanation shown to the user. <see langword="null"/> if allowed.</summary>
    public string? Reason { get; init; }
}

/// <summary>The consumption of a completed run to add to the quota counters.</summary>
public sealed record QuotaConsumption
{
    /// <summary>The tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>The name of the agent that ran.</summary>
    public required string AgentName { get; init; }

    /// <summary>The number of runs to add. Normally 1.</summary>
    public long Runs { get; init; } = 1;

    /// <summary>The total tokens to add.</summary>
    public long Tokens { get; init; }

    /// <summary>
    /// The amount to add. <see langword="null"/> if pricing is undefined —
    /// <strong>not</strong> zero: AgentPrism does not invent a price.
    /// </summary>
    public decimal? Cost { get; init; }

    /// <summary>The moment the consumption occurred (UTC). The period is computed from this.</summary>
    public required DateTimeOffset OccurredAt { get; init; }
}

/// <summary>A filter for querying quota usage.</summary>
public sealed record QuotaUsageQuery
{
    /// <summary>The tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>Fetches only this agent's counters. An empty string means tenant-wide.</summary>
    public string? AgentName { get; init; }

    /// <summary>Fetches only this interval's counters.</summary>
    public QuotaPeriod? Period { get; init; }

    /// <summary>The moment the counters belong to (UTC). The current time is used if not given.</summary>
    public DateTimeOffset? AsOf { get; init; }
}
