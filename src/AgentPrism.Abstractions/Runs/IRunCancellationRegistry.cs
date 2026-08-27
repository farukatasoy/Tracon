namespace AgentPrism;

/// <summary>
/// The in-memory registry of runs in progress on this instance; binds an
/// incoming cancellation request to the running run's
/// <see cref="System.Threading.CancellationTokenSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// The registry is <strong>in-process</strong>. In a multi-instance
/// deployment, a request may land on the wrong instance; in that case the
/// cancellation endpoint returns <c>409 Conflict</c>. A setup that needs a
/// distributed registry can replace this interface with its own
/// implementation (<c>TryAddSingleton</c>).
/// </para>
/// <para>
/// Cancelling the root run (<c>RunId == RootRunId</c>) also cancels every
/// record under the same <c>RootRunId</c>. Cancelling a single child run
/// affects neither sibling branches nor the root.
/// </para>
/// <para>
/// <strong>Tenant behavior.</strong>
/// <see cref="Register"/> and <see cref="TryCancel"/> take an EXPECTED
/// tenant as an explicit parameter — never the ambient tenant, since the
/// caller's own thread may not belong to the run's tenant.
/// <see cref="ActiveCount"/> and <see cref="ActiveRunIds"/> are
/// TENANT-INDEPENDENT: this is in-process diagnostic state covering every
/// tenant's runs on this instance at once, by design (<c>RunHeartbeatWriter</c>
/// needs the whole set, not one tenant's slice).
/// </para>
/// </remarks>
public interface IRunCancellationRegistry
{
    /// <summary>Writes a running run into the registry.</summary>
    /// <param name="runId">The run's identifier.</param>
    /// <param name="rootRunId">
    /// The identifier of the run at the root of the tree. Equals
    /// <paramref name="runId"/>
    /// for the root.
    /// </param>
    /// <param name="tenantId">The run's tenant.</param>
    /// <param name="source">
    /// The source that triggers the run's cancellation. Ownership stays with the
    /// caller; this method does not dispose it.
    /// </param>
    /// <returns>The object to dispose to remove the record from the registry.</returns>
    IDisposable Register(Guid runId, Guid rootRunId, string? tenantId, CancellationTokenSource source);

    /// <summary>Requests a run's cancellation. If it is the tree root, child runs are also cancelled.</summary>
    /// <param name="runId">The identifier of the run whose cancellation is requested.</param>
    /// <param name="tenantId">The requesting tenant. If it does not match the record's tenant, the request is ignored.</param>
    /// <returns><see langword="true"/> if the cancellation request reached a record.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Guarantee limit: cancellation is cooperative, not forced.</strong> This
    /// method only calls <see cref="System.Threading.CancellationTokenSource.Cancel()"/>
    /// on the token registered with the matching <see cref="Register"/> call; it does
    /// not force the run's body to stop. A <see langword="true"/> return means the
    /// signal reached a registered record, NOT that the run has actually stopped — a
    /// run body that reads its token late, catches
    /// <see cref="OperationCanceledException"/>, or ignores the token altogether keeps
    /// running (and, if it calls a billed model provider, keeps accruing cost) until it
    /// chooses to observe the cancellation.
    /// </para>
    /// </remarks>
    bool TryCancel(Guid runId, string? tenantId);

    /// <summary>The number of runs in progress on this instance. For diagnostics and tests.</summary>
    int ActiveCount { get; }

    /// <summary>
    /// The identifiers of the runs currently in progress on this instance.
    /// </summary>
    /// <remarks>
    /// <c>RunHeartbeatWriter</c> uses this list to write heartbeats only for runs
    /// THIS process is actually executing. Another instance marking a Running row as
    /// "alive" by mistake would defeat the entire purpose of reconciliation.
    /// </remarks>
    IReadOnlyCollection<Guid> ActiveRunIds { get; }
}
