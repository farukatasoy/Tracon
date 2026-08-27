namespace AgentPrism;

/// <summary>
/// Receives run events as they are written, in addition to <see cref="IRunStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Runs on the hot path.</strong> <see cref="OnEventAsync"/> is awaited
/// before the run's response continues streaming to its own caller — queue the
/// event and return, do not block on further I/O.
/// </para>
/// <para>
/// <strong>A sink failure never fails the run.</strong> The writer that dispatches
/// to this sink logs the exception, disables the sink for the rest of the run and
/// keeps going — the store write and every other registered sink are unaffected.
/// </para>
/// <para>
/// <strong>One instance serves every concurrent run.</strong> A sink is resolved
/// once (typically as a singleton) and its <see cref="OnEventAsync"/> is called
/// from as many runs as are in flight at once; implementations must be thread-safe.
/// </para>
/// <para>
/// Events from every tenant pass through the same sink instance; use
/// <c>RunEvent.TenantId</c> to tell them apart.
/// </para>
/// <para>
/// <strong>Tenant behavior — EXPECTED tenant.</strong> The tenant is carried
/// by <see cref="RunEvent.TenantId"/> on the event itself, the same way
/// <see cref="IRunStore.AppendEventAsync"/> reads it — never from the
/// ambient tenant, since the writing thread may not belong to the event's
/// own tenant.
/// </para>
/// </remarks>
public interface IRunEventSink
{
    /// <summary>Called for every event a run writes, in sequence order.</summary>
    /// <param name="runEvent">The event, with its sequence number and timestamp already assigned.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default);
}
