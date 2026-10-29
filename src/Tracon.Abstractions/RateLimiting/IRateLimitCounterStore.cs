namespace Tracon;

/// <summary>
/// The counter store behind <see cref="RateLimitScope.Deployment"/>: it counts the requests
/// of one rate-limit partition inside one fixed window.
/// </summary>
/// <remarks>
/// <para>
/// The store only counts. Tracon owns the algorithm: it aligns the window, compares the
/// count with the permit limit and computes <c>Retry-After</c>. A consumer can therefore
/// implement the seam in a few lines — for Redis, <c>INCR</c> plus <c>EXPIRE</c> on a key
/// built from the partition key and the window start.
/// </para>
/// <para>
/// <strong>The partition key is opaque.</strong> Tracon builds it
/// (<c>endpoint:tracon:tenant:acme</c>, <c>endpoint:agui:global</c>,
/// <c>trigger:&lt;id&gt;</c>). A store must not parse it, must not change it and must
/// not truncate it. It is at most a few hundred characters long.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered as a singleton with <c>TryAdd</c>;
/// a consumer's own registration wins. Every SQL provider package replaces the in-memory
/// default with its own store.
/// </para>
/// <para>
/// <strong>Tenant behavior — TENANT-INDEPENDENT.</strong> The tenant is part of the
/// partition key when the limit is partitioned by tenant; the store has no tenant
/// parameter and keeps no tenant column.
/// </para>
/// <para>
/// <strong>Delivery — no delivery guarantee applies.</strong> A call either returns the
/// count after the add or throws. A call that throws may or may not have added one: a
/// request cancelled after the add still counts, which a fixed window accepts. Tracon
/// does not retry; it applies the instance's local limit for that request instead.
/// </para>
/// <para>
/// <strong>The count must be atomic.</strong> Two instances that call
/// <see cref="IncrementAsync"/> at the same time must both be counted. A read followed by a
/// write does not satisfy this. The contract tests in
/// <c>Tracon.Testing.Contracts.Xunit</c> (<c>RateLimitCounterStoreContract</c>) check it
/// with parallel calls; run them against every implementation.
/// </para>
/// </remarks>
public interface IRateLimitCounterStore
{
    /// <summary>
    /// Adds one to the counter of a partition's window and returns the counter after the add.
    /// </summary>
    /// <param name="partitionKey">The opaque partition key Tracon built.</param>
    /// <param name="windowStart">
    /// The start of the window this call belongs to (UTC). Every instance computes the same
    /// value for the same moment.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The window the count belongs to and the count after the add. The store keeps one
    /// window per partition. When <paramref name="windowStart"/> is later than the stored
    /// window, the stored window moves forward and the count restarts at one. When it is
    /// earlier (an instance with a slow clock), the stored window does not move back: the
    /// call is counted in the stored window and the stored window is returned.
    /// </returns>
    ValueTask<RateLimitWindowCount> IncrementAsync(
        string partitionKey,
        DateTimeOffset windowStart,
        CancellationToken cancellationToken = default);
}
