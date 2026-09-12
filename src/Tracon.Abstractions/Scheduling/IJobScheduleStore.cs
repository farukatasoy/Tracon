namespace Tracon;

/// <summary>The store for schedule definitions.</summary>
/// <remarks>
/// Separate from <see cref="IJobStore"/>: this store holds only the
/// <em>definitions</em> (when, what runs); the actual queued jobs are in
/// <see cref="IJobStore"/>. The split is the same as between
/// <see cref="IWorkflowDefinitionStore"/> and <see cref="IWorkflowCheckpointStore"/>.
/// </remarks>
public interface IJobScheduleStore
{
    /// <summary>Fetches the schedule with the given name.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The schedule name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The schedule; <see langword="null"/> if it does not exist.</returns>
    ValueTask<JobSchedule?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>Lists all of a tenant's schedules, by name.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The schedules.</returns>
    ValueTask<IReadOnlyList<JobSchedule>> ListAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates the schedule (name + tenant is unique).</summary>
    /// <param name="schedule">The schedule to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The saved schedule.</returns>
    ValueTask<JobSchedule> SaveAsync(JobSchedule schedule, CancellationToken cancellationToken = default);

    /// <summary>Deletes the schedule.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The schedule name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the delete happened.</returns>
    ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the schedules of every tenant that are due to run
    /// (<c>NextRunAt &lt;= asOfUtc</c>), enabled, and carry a cron expression.
    /// </summary>
    /// <param name="asOfUtc">The comparison moment (UTC).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The due schedules.</returns>
    ValueTask<IReadOnlyList<JobSchedule>> ListDueAsync(DateTimeOffset asOfUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to atomically advance a schedule's <see cref="JobSchedule.NextRunAt"/>
    /// field (compare-and-swap).
    /// </summary>
    /// <param name="scheduleId">The schedule identifier.</param>
    /// <param name="expectedNextRunAt">
    /// The expected current value. If another instance has already advanced
    /// it, the value does not match and the operation fails — this is how two
    /// overlapping scheduling triggers are prevented.
    /// </param>
    /// <param name="newNextRunAt">The new next-run time.</param>
    /// <param name="ranAt">The time this trigger happened (UTC).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the claim succeeded.</returns>
    ValueTask<bool> TryClaimNextRunAsync(
        Guid scheduleId,
        DateTimeOffset expectedNextRunAt,
        DateTimeOffset newNextRunAt,
        DateTimeOffset ranAt,
        CancellationToken cancellationToken = default);
}
