namespace AgentPrism;

/// <summary>Rate-limit settings applied to AgentPrism endpoints.</summary>
/// <remarks>
/// <para>
/// Read from the <c>AgentPrism:RateLimit</c> configuration section.
/// </para>
/// <para>
/// <strong>Off by default</strong>. It has no way of knowing a
/// library consumer's traffic; a default that comes on would silently answer an
/// upgrading setup's live traffic with <c>429</c>. Recommended values are documented in the README.
/// </para>
/// <para>
/// Rate limiting <strong>is not a quota</strong>: it smooths out sudden load at
/// the second/minute scale and lives in memory. Total-consumption limiting at
/// the day/month scale is done with <see cref="AgentPrismQuotaOptions"/> and
/// counted in the database.
/// </para>
/// <para>
/// The counter lives in the memory of a single process: AgentPrism ships no
/// distributed rate limiter. In a multi-instance deployment the limit therefore
/// applies <strong>per instance</strong> — two instances with a
/// <see cref="PermitLimit"/> of 100 together admit 200 requests per window, and
/// <see cref="RateLimitPartitionKind.Tenant"/> partitions that per-instance
/// window per tenant rather than across the deployment.
/// <see cref="InboundTriggerRateLimiter"/> has the same scope. A ceiling that
/// must hold for the deployment as a whole is a total-consumption question, so
/// it belongs to <see cref="AgentPrismQuotaOptions"/>.
/// </para>
/// </remarks>
public sealed class AgentPrismRateLimitOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "AgentPrism:RateLimit";

    /// <summary>Whether rate limiting is enabled. Default <see langword="false"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>The number of requests allowed within a window.</summary>
    public int PermitLimit { get; set; } = 60;

    /// <summary>The length of the window.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The number of requests to queue once the limit is exceeded. If <c>0</c>,
    /// a request gets <c>429</c> immediately.
    /// </summary>
    public int QueueLimit { get; set; }

    /// <summary>
    /// Which key the limit is partitioned by. Default is per tenant.
    /// </summary>
    public RateLimitPartitionKind Partition { get; set; } = RateLimitPartitionKind.Tenant;
}

/// <summary>Which key the rate limit is partitioned by.</summary>
public enum RateLimitPartitionKind
{
    /// <summary>Each tenant gets its own quota.</summary>
    Tenant = 0,

    /// <summary>The limit is shared across the whole deployment.</summary>
    Global = 1,
}
