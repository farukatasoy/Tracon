namespace Tracon;

/// <summary>Rate-limit settings applied to Tracon endpoints.</summary>
/// <remarks>
/// <para>
/// Read from the <c>Tracon:RateLimit</c> configuration section.
/// </para>
/// <para>
/// <strong>Off by default</strong>. It has no way of knowing a
/// library consumer's traffic; a default that comes on would silently answer an
/// upgrading setup's live traffic with <c>429</c>. Recommended values are documented in the README.
/// </para>
/// <para>
/// Rate limiting <strong>is not a quota</strong>: it smooths out sudden load at
/// the second/minute scale and by default lives in memory. Total-consumption limiting at
/// the day/month scale is done with <see cref="TraconQuotaOptions"/> and
/// counted in the database.
/// </para>
/// <para>
/// <strong>Where the counter lives is a choice</strong> (<see cref="Scope"/>). By default it
/// lives in the memory of each process: Tracon then keeps no shared counter, the limit applies
/// <strong>per instance</strong> — two instances with a <see cref="PermitLimit"/> of 100 together
/// admit 200 requests per window — and <see cref="RateLimitPartitionKind.Tenant"/> partitions
/// that per-instance window per tenant. With <see cref="RateLimitScope.Deployment"/> the counter
/// lives in the <see cref="IRateLimitCounterStore"/>, so the limit holds for the deployment as a
/// whole; each request then costs one database write.
/// The inbound trigger rate limit follows the same choice.
/// The management API group and the AG-UI group each keep their own counter. A ceiling on total
/// consumption over a day or a month belongs to <see cref="TraconQuotaOptions"/>.
/// </para>
/// </remarks>
public sealed class TraconRateLimitOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Tracon:RateLimit";

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
    /// <remarks>
    /// Only <see cref="RateLimitScope.Instance"/> can queue. A database counter has no waiting
    /// line, so a value above zero together with <see cref="RateLimitScope.Deployment"/> stops the
    /// application at start.
    /// </remarks>
    public int QueueLimit { get; set; }

    /// <summary>
    /// Which key the limit is partitioned by. Default is per tenant.
    /// </summary>
    public RateLimitPartitionKind Partition { get; set; } = RateLimitPartitionKind.Tenant;

    /// <summary>
    /// Where the requests are counted. Default <see cref="RateLimitScope.Instance"/>: in the memory
    /// of each process, with no database call.
    /// </summary>
    /// <remarks>
    /// The setting covers both rate limiters: the one on the endpoint groups and the one on the
    /// inbound trigger accept endpoint. With <see cref="RateLimitScope.Deployment"/> and no shared
    /// counter store (no SQL provider, no store of the consumer's own), the counter is still in
    /// process memory and the application logs a warning at start.
    /// </remarks>
    public RateLimitScope Scope { get; set; } = RateLimitScope.Instance;
}

/// <summary>Which key the rate limit is partitioned by.</summary>
public enum RateLimitPartitionKind
{
    /// <summary>Each tenant gets its own quota.</summary>
    Tenant = 0,

    /// <summary>
    /// One quota for every caller. The quota is shared across the whole deployment only when
    /// <see cref="TraconRateLimitOptions.Scope"/> is <see cref="RateLimitScope.Deployment"/>;
    /// with <see cref="RateLimitScope.Instance"/> each instance keeps its own.
    /// </summary>
    Global = 1,
}
