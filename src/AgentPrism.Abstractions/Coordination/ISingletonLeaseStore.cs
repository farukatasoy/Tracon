namespace AgentPrism;

/// <summary>
/// The lease store that keeps a named job running on a single instance across the
/// cluster.
/// </summary>
/// <remarks>
/// <para>
/// Single-executor election uses it to coordinate background jobs such as MCP
/// discovery and model health probing, which must not repeat on several replicas. The
/// lease is a table, not a session lock: it behaves the same way on all three SQL
/// providers (PostgreSQL, SQL Server, SQLite), and SQLite has no session lock
/// equivalent.
/// </para>
/// <para>
/// While <see cref="SingletonExecutionOptions.Enabled"/> is <see langword="false"/> (the
/// default) this store is never called — a single-instance setup pays no extra database
/// round trip.
/// </para>
/// </remarks>
public interface ISingletonLeaseStore
{
    /// <summary>
    /// Tries to take the lease. It returns <see langword="false"/> when someone else
    /// holds the lease and it has not expired.
    /// </summary>
    /// <param name="name">The name of the lease (the id of the protected job).</param>
    /// <param name="ownerId">The id of this instance.</param>
    /// <param name="duration">How long the lease stays valid.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the lease was taken.</returns>
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
    /// <param name="ownerId">The id of the instance that claims to hold the lease.</param>
    /// <param name="duration">The new lease duration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the renewal succeeded.</returns>
    ValueTask<bool> RenewAsync(
        string name,
        string ownerId,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    /// <summary>Releases the lease. It does nothing when the caller is not the owner.</summary>
    /// <param name="name">The name of the lease.</param>
    /// <param name="ownerId">The id of the instance that claims to hold the lease.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the work is done.</returns>
    ValueTask ReleaseAsync(
        string name,
        string ownerId,
        CancellationToken cancellationToken = default);
}
