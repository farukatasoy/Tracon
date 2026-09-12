namespace Tracon;

/// <summary>Reports whether the process has begun a graceful shutdown drain.</summary>
/// <remarks>
/// <para>
/// Backed by <c>TraconDrainService</c> (Tracon.Core), always
/// registered regardless of <c>TraconDrainOptions.Enabled</c> — while
/// disabled, <see cref="IsDraining"/> simply never becomes
/// <see langword="true"/>. Consumers that start a run (the HTTP run
/// endpoints, the job worker) check this before accepting new work.
/// </para>
/// <para>
/// <strong>Tenant behavior — TENANT-INDEPENDENT.</strong> Draining is a
/// process-wide condition; it has no tenant concept and applies identically
/// regardless of which tenant a new run would belong to.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered as a singleton with
/// <c>TryAdd</c>; a consumer's own registration wins.
/// </para>
/// <para>
/// <strong>Guarantee limit: <see cref="IsDraining"/> does not itself
/// guarantee that no new run starts after it flips.</strong> This interface
/// is a READ of the flag only; the register/accept window between a caller
/// checking <see cref="IsDraining"/> and actually registering a run with
/// <c>IRunCancellationRegistry</c> is not closed by this interface at all.
/// What actually keeps an in-flight run from being cut off mid-shutdown is
/// TWO SEPARATE backup mechanism layers this interface does not control:
/// the HTTP host's own Kestrel request draining (which waits for an
/// in-flight request to finish before the process stops), and the job
/// worker's own <c>WaitForRunningJobsAsync</c> drain loop. A setup that
/// relies on this flag ALONE, without one of those two layers in its own
/// request path, has no closed guarantee against a run starting in that
/// narrow window (BL-026; see <c>TraconDrainOptions.Enabled</c> too).
/// </para>
/// </remarks>
public interface ITraconDrainState
{
    /// <summary>
    /// Gets whether the process has begun stopping and is waiting for
    /// in-flight runs to finish. New runs should not be accepted while this
    /// is <see langword="true"/>.
    /// </summary>
    bool IsDraining { get; }
}
