namespace Tracon;

/// <summary>
/// The lease store that keeps a named job running on a single instance across the
/// cluster.
/// </summary>
/// <remarks>
/// <para>
/// Single-executor election uses it to coordinate background jobs such as model
/// health probing, which must not repeat on several replicas. The MCP OAuth token refresh
/// uses it too, to let one instance at a time refresh a token (the lease is named for the
/// server, not for a job that runs on a schedule). The
/// lease is a table, not a session lock: it behaves the same way on all three SQL
/// providers (PostgreSQL, SQL Server, SQLite), and SQLite has no session lock
/// equivalent.
/// </para>
/// <para>
/// While <see cref="SingletonExecutionOptions.Enabled"/> is <see langword="false"/> (the
/// default) this store is never called — a single-instance setup pays no extra database
/// round trip.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered as a singleton with
/// <c>TryAdd</c>; a consumer's own registration wins.
/// </para>
/// <para>
/// <strong>Tenant behavior — TENANT-INDEPENDENT.</strong> A lease name identifies a
/// protected JOB (<c>"run-reconciliation"</c>, a health-probe job id) or action, not a tenant; leases
/// are not scoped by tenant at all.
/// </para>
/// <para>
/// <strong>Guarantee limit: this lease is eventually correct, NOT strictly
/// exclusive.</strong> It gives no hard mutual-exclusion guarantee the way a
/// database advisory lock or session lock would — this is deliberate (SQLite has no
/// session-lock equivalent, so the lease had to be a plain table row instead). A lease
/// owner that freezes past its own <c>duration</c> without releasing it — a GC pause,
/// thread starvation, or a network partition that cuts it off from the store — leaves a
/// narrow split-brain window: <see cref="TryAcquireAsync"/> lets a SECOND instance take
/// the same lease once it expires, while the frozen first owner may resume and believe
/// it still holds it, until its own next <see cref="RenewAsync"/> call (correctly)
/// reports it lost the lease. A consumer whose protected job is not idempotent under a
/// brief overlap must not rely on this store alone.
/// </para>
/// <para>
/// <strong>The clock that ends a lease.</strong> The PostgreSQL and SQL Server stores
/// measure a lease with the database server's clock, read inside the statement: the
/// clocks of the instances that share the database do not enter the decision, so an
/// instance whose clock is ahead cannot take a lease that is live. What remains are the
/// causes above (a pause, a partition) and the server's own clock. The in-memory store
/// and SQLite are used by one process and measure with the clock they were given
/// (<see cref="TimeProvider"/>), on its monotonic reading: a lease is a span of time, so a
/// wall clock that a time service steps forward does not end a lease whose owner still holds
/// it, and one stepped back does not keep a lease alive past the span it was taken for.
/// </para>
/// </remarks>
public interface ISingletonLeaseStore
{
    /// <summary>
    /// Tries to take the lease. It returns <see langword="false"/> when someone else
    /// holds the lease and it has not expired.
    /// </summary>
    /// <param name="name">The name of the lease (the id of the protected job).</param>
    /// <param name="ownerId">The id of this instance. It is compared by its characters, letter case included; SQL Server still ignores trailing blanks.</param>
    /// <param name="duration">
    /// How long the lease stays valid: from one millisecond to <c>2147483647000</c> milliseconds (a little over 68
    /// years). <see cref="Timeout.InfiniteTimeSpan"/> is minus one millisecond, not a lease that never ends.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the lease was taken.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="duration"/> is outside that range. The call checks it before anything else, before the
    /// cancellation token as well, and writes nothing.
    /// </exception>
    ValueTask<bool> TryAcquireAsync(
        string name,
        string ownerId,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extends the lease that is held. It returns <see langword="false"/> when the lease
    /// has moved to someone else — the caller MUST then drop the job.
    /// </summary>
    /// <param name="name">The name of the lease.</param>
    /// <param name="ownerId">The id of the instance that claims to hold the lease, compared by its characters, letter case included.</param>
    /// <param name="duration">
    /// The new lease duration, in the range <see cref="TryAcquireAsync"/> accepts: from one millisecond to
    /// <c>2147483647000</c> milliseconds.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the renewal succeeded.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="duration"/> is outside that range. The lease stays as it was.
    /// </exception>
    ValueTask<bool> RenewAsync(
        string name,
        string ownerId,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    /// <summary>Releases the lease. It does nothing when the caller is not the owner.</summary>
    /// <param name="name">The name of the lease.</param>
    /// <param name="ownerId">The id of the instance that claims to hold the lease, compared by its characters, letter case included.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the work is done.</returns>
    ValueTask ReleaseAsync(
        string name,
        string ownerId,
        CancellationToken cancellationToken = default);
}
