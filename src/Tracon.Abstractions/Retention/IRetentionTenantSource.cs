namespace Tracon;

/// <summary>
/// Names the tenants a retention fan-out job sweeps: the one place that knows which tenants the installation has.
/// </summary>
/// <remarks>
/// <para>
/// A retention job cleans <strong>one tenant</strong>: the tenant that queued it. A schedule belongs to one tenant too,
/// so a single schedule would clean only its own tenant and leave every other tenant's old rows where they are. The
/// built-in handler <see cref="JobHandlerKeys.RetentionFanOut"/> closes that gap. It runs in the host tenant, asks
/// this interface for the tenants, and queues one <see cref="JobHandlerKeys.Retention"/> job for each of them. Each of
/// those jobs reads its own tenant's policy, so a tenant that keeps its data for a year and a tenant whose policy is
/// switched off are both honoured. Register one schedule for <see cref="JobHandlerKeys.RetentionFanOut"/> in the host
/// tenant and every tenant is cleaned.
/// </para>
/// <para>
/// The built-in source lists the tenants that are registered (<see cref="ITenantStore"/>), the tenants that have a
/// retention policy of their own, and the host tenant. A tenant that is in none of those (a tenant nobody registered
/// and that never saved a policy) is not swept by the built-in source, because Tracon has no other way to learn that
/// it exists. A host that keeps its tenants elsewhere registers its own source and the built-in one steps aside.
/// </para>
/// <para>
/// <strong>Lifetime and threading:</strong> registered as a singleton with <c>TryAdd</c>; a consumer's own
/// registration wins. It is called from the fan-out job, one call at a time for each job, and two jobs can call it at
/// once.
/// </para>
/// <para>
/// <strong>Tenant:</strong> tenant-independent. The call lists tenants and runs for none of them, and it asks for no
/// ambient tenant. The caller is trusted host code inside the process: this interface is not an HTTP surface and has
/// no authorization of its own. An identifier that is not a valid tenant identifier (the <c>"*"</c> default marker, an
/// empty value, a character outside the tenant alphabet) is skipped and counted by the job. The job lower-cases the
/// rest and queues each tenant once, however many times the source names it.
/// </para>
/// <para>
/// <strong>Delivery:</strong> no delivery guarantee applies. The source only reads. The fan-out job that calls it is
/// <c>at-least-once</c> (see <see cref="IJobHandler"/>), so a source can be read again for the same schedule time.
/// A source that throws fails that job attempt, and the job is tried again; a tenant the source did not name is not
/// swept until the next run.
/// </para>
/// <para>
/// The example reads the tenants from the host's own tenant directory. Register the source with
/// <c>builder.Services.AddSingleton&lt;IRetentionTenantSource, DirectoryRetentionTenantSource&gt;()</c> before
/// <c>builder.AddTracon()</c>, so that it wins outright.
/// </para>
/// <example>
/// <code>
/// public sealed class DirectoryRetentionTenantSource(ITenantDirectory directory) : IRetentionTenantSource
/// {
///     public async IAsyncEnumerable&lt;string&gt; ListTenantsAsync(
///         [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
///     {
///         await foreach (var key in directory.ListTenantKeysAsync(cancellationToken))
///         {
///             yield return key;
///         }
///     }
/// }
/// </code>
/// </example>
/// </remarks>
public interface IRetentionTenantSource
{
    /// <summary>Lists the tenant identifiers a retention fan-out job sweeps.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The tenant identifiers, in no particular order. The same identifier may appear more than once. An identifier
    /// that is not valid is skipped by the job.
    /// </returns>
    IAsyncEnumerable<string> ListTenantsAsync(CancellationToken cancellationToken = default);
}
