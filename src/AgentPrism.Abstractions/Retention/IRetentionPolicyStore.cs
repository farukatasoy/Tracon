namespace AgentPrism;

/// <summary>The control-plane store for retention policies and run history.</summary>
/// <remarks>
/// <para>
/// The data plane (actual delete/count/archive-read) goes through
/// <see cref="IRetentionStore"/>. This split matches the
/// <c>IJobStore</c>/<c>IJobScheduleStore</c> split in the job queue: the
/// control plane is always registered (in-memory or SQL), while the data
/// plane is meaningful only when a SQL provider is enabled.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered as a singleton with
/// <c>TryAdd</c>; a consumer's own registration wins.
/// </para>
/// </remarks>
public interface IRetentionPolicyStore
{
    /// <summary>Lists all of a tenant's policies (and the <c>"*"</c> tenant-wide ones).</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The policies.</returns>
    ValueTask<IReadOnlyList<RetentionPolicy>> ListPoliciesAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Looks up a single target's policy within the tenant, falling back to the <c>"*"</c> tenant-wide policy.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="target">The target name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The policy; <see langword="null"/> if none exists at either level.</returns>
    ValueTask<RetentionPolicy?> GetPolicyAsync(
        string tenantId,
        string target,
        CancellationToken cancellationToken = default);

    /// <summary>Creates or updates a policy.</summary>
    /// <param name="policy">The policy.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The saved policy.</returns>
    ValueTask<RetentionPolicy> SavePolicyAsync(
        RetentionPolicy policy,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a policy.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="target">The target name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the delete happened.</returns>
    ValueTask<bool> DeletePolicyAsync(
        string tenantId,
        string target,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a new cleanup run.</summary>
    /// <param name="run">The starting record. <see cref="RetentionRun.CompletedAt"/> is ignored.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created record.</returns>
    ValueTask<RetentionRun> CreateRunAsync(RetentionRun run, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments an in-progress run's counters. Called after each batch.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <param name="deletedDelta">The number of rows deleted in this batch.</param>
    /// <param name="archivedDelta">The number of rows archived in this batch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    ValueTask AppendRunProgressAsync(
        Guid runId,
        long deletedDelta,
        long archivedDelta,
        CancellationToken cancellationToken = default);

    /// <summary>Finalizes a run.</summary>
    /// <param name="runId">The run identifier.</param>
    /// <param name="completedAt">The completion time (UTC).</param>
    /// <param name="errorMessage">The error message; <see langword="null"/> if successful.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    ValueTask CompleteRunAsync(
        Guid runId,
        DateTimeOffset completedAt,
        string? errorMessage,
        CancellationToken cancellationToken = default);

    /// <summary>Lists a tenant's run history. The newest record is returned first.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="target">Fetches only this target's runs; all if <see langword="null"/>.</param>
    /// <param name="skip">The number of records to skip.</param>
    /// <param name="take">The maximum number of records to fetch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The records.</returns>
    ValueTask<IReadOnlyList<RetentionRun>> ListRunsAsync(
        string tenantId,
        string? target,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}
