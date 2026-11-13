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
/// <para>
/// <strong>A counter may be deleted once its window is over, and not before.</strong> The next
/// call for a partition with no counter starts at one, which is what a later window does anyway
/// (the contract tests check that a later window restarts the count). A counter deleted inside
/// its window restarts the count there and lifts the limit for the rest of that window. The store
/// is not told how long a window is, so an expiry has to be longer than the longest window
/// configured (the inbound trigger limit uses one minute).
/// </para>
/// <para>
/// <strong>Who deletes.</strong> A store that expires its own counters (a Redis key with a time to
/// live) needs nothing more. A store that keeps rows until someone removes them implements
/// <see cref="RemoveEndedWindowsAsync"/>: Tracon calls it about once per longest window with the
/// moment before which no window can still be open, so the table follows the partitions that are
/// counting and not the partitions that ever were. The partition key carries the tenant, and an
/// installation that accepts any tenant identifier lets a caller make up partitions.
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

    /// <summary>
    /// Removes the counters whose window started before a moment, so that a store that keeps rows does not keep
    /// one for every partition that ever counted.
    /// </summary>
    /// <param name="startedBefore">
    /// A counter whose stored window started <em>strictly before</em> this moment is removed; one that started at it or
    /// later stays. Tracon passes the current time minus the longest window it has counted in, so a counter older than
    /// this is over for every partition.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many counters were removed; <c>0</c> when none was or the store removes nothing.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Optional.</strong> The default removes nothing, so a store written before this member existed keeps
    /// working and keeps its rows. A store whose counters expire by themselves leaves it alone.
    /// </para>
    /// <para>
    /// <strong>Deleting is safe in this window and only in this window.</strong> The next call for a removed partition
    /// counts from one, which is what its next window does anyway. A store must not remove a counter whose window
    /// started at <paramref name="startedBefore"/> or later: that would lift the limit for a window that is open.
    /// A window that starts in the future (an instance whose clock runs ahead) is not over and stays.
    /// </para>
    /// <para>
    /// A failure is logged and counted by Tracon and does not fail the request that triggered the call; the next call
    /// removes what this one left.
    /// </para>
    /// </remarks>
    ValueTask<int> RemoveEndedWindowsAsync(DateTimeOffset startedBefore, CancellationToken cancellationToken = default)
        => new(0);
}
