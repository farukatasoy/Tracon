namespace Tracon;

/// <summary>
/// Opens your application's own tenant scope around a job that Tracon's job worker runs for a tenant.
/// </summary>
/// <remarks>
/// <para>
/// A queued or scheduled run starts on Tracon's job worker, with no request. The worker opens
/// <see cref="AmbientTenantScope"/> for the job's tenant, so Tracon's own code reads the right tenant. The scope of the
/// framework your application uses for its own tenant-filtered data (the current tenant of a multi-tenant framework, a
/// <c>AsyncLocal</c> of your own) does not change: a tool that reads that data sees no tenant, and reads the rows that
/// belong to none. Without this interface, each tool opens the framework's scope itself from
/// <c>TraconRunContext.Current.TenantId</c>. With it registered, the worker opens it once, around the whole job.
/// </para>
/// <para>
/// The worker calls <see cref="Begin"/> for the job's tenant after it opens its own scope and before it creates the
/// container scope the job runs in, and disposes the result when the job ends, whatever way it ends. Everything the
/// job runs sees it: the handler, the agent's tools and decorators, and the sub-agents it calls. Only the job worker
/// calls it. A run that a request starts already runs inside your framework's own scope, which your request middleware
/// opened, and the other background paths of Tracon run no tool.
/// </para>
///
/// <para><strong>Lifetime and threading</strong></para>
/// <para>
/// An implementation is a <strong>singleton</strong>, resolved once when the worker starts, and it is called
/// <strong>concurrently</strong>: two jobs of two tenants can begin at the same moment. It must not capture a scoped
/// service. <see cref="Begin"/> is synchronous on purpose. It runs in the worker's own method body, so a value it
/// writes to an <c>AsyncLocal</c> reaches the job; an asynchronous method would lose the value on its way back.
/// </para>
///
/// <para><strong>Tenant mode</strong></para>
/// <para>
/// The tenant is the argument, not the ambient tenant: <see cref="Begin"/> is called inside Tracon's ambient tenant
/// scope, which already holds the same value, and the implementation maps the identifier to its own key. The identifier
/// is the canonical form (<see cref="AmbientTenantScope.Normalize"/>): lower case, never empty.
/// </para>
///
/// <para><strong>Failures and delivery</strong></para>
/// <para>
/// A <see cref="Begin"/> that throws fails the job before it runs, with <see cref="JobErrorCodes.HostTenantScopeFailed"/>,
/// and the job is not retried: an application that does not know the tenant will not know it on the next attempt either.
/// A <see cref="IDisposable.Dispose"/> that throws is logged and changes nothing about the job's result. No delivery
/// guarantee applies to the scope itself: it is opened for each attempt of a job, so a job that is leased again opens it again.
/// </para>
/// <para>
/// Register it with <c>services.AddSingleton&lt;IHostTenantScope, YourTenantScope&gt;()</c>; there is no default, and
/// without a registration the worker calls nothing.
/// </para>
/// <example>
/// <code>
/// public sealed class AcmeTenantScope(Func&lt;string, IDisposable&gt; changeCurrentTenant) : IHostTenantScope
/// {
///     // The framework's own call, here a delegate, opens its scope for the tenant of the job.
///     public IDisposable? Begin(string tenantId) =&gt; changeCurrentTenant(tenantId);
/// }
/// </code>
/// </example>
/// </remarks>
public interface IHostTenantScope
{
    /// <summary>Opens the host's tenant scope for a job of <paramref name="tenantId"/>.</summary>
    /// <param name="tenantId">The tenant of the job, in the canonical form of <see cref="AmbientTenantScope.Normalize"/>.</param>
    /// <returns>
    /// The scope to dispose when the job ends, or <see langword="null"/> when there is no scope to open for this tenant.
    /// </returns>
    /// <exception cref="Exception">
    /// Any exception fails the job before it runs (<see cref="JobErrorCodes.HostTenantScopeFailed"/>).
    /// </exception>
    IDisposable? Begin(string tenantId);
}
