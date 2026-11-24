namespace Tracon;

/// <summary>
/// Reads what a retention run would delete for the current tenant, without deleting anything.
/// </summary>
/// <remarks>
/// <para>
/// It answers the question <c>GET /api/retention/preview</c> answers, from host code: for each target, which policy
/// applies to this tenant, the cutoff date that policy gives, and how many rows are older than it. The count is what a
/// run would delete now. The call reads the tenant's own policy first, then the <c>"*"</c> default every tenant
/// without a policy of its own is given, then the configuration default. It changes nothing.
/// </para>
/// <para>
/// Deleting stays on the job queue. A run that deletes goes through <see cref="JobHandlerKeys.Retention"/> (or
/// <see cref="JobHandlerKeys.RetentionFanOut"/> for every tenant) so that it keeps its lease, its retries, its lane
/// and its time limit. This interface has no method that deletes, and it is not the way to start one.
/// </para>
/// <para>
/// <strong>Lifetime and threading:</strong> registered as a singleton with <c>TryAdd</c> and safe to call from any
/// thread; a consumer's own registration wins.
/// </para>
/// <para>
/// <strong>Tenant:</strong> ambient tenant. There is no tenant parameter: the call reads the tenant of the request or
/// job it runs in (<see cref="ITenantContext"/>, which reports <see cref="AmbientTenantScope"/> while one is open),
/// so a call cannot ask about another tenant's rows. Outside a request and outside an open scope (a hosted
/// service, a startup task) the context answers with the default tenant (<c>TraconOptions.DefaultTenantId</c>), and
/// the call previews that tenant's rows without an error: a host that serves several tenants opens
/// <see cref="AmbientTenantScope.Begin"/> for each tenant it previews. A tenant the context cannot name as a valid tenant
/// identifier (the <c>"*"</c> default marker, a character outside the tenant alphabet) is refused with an
/// <see cref="ArgumentException"/>.
/// </para>
/// <para>
/// <strong>Delivery:</strong> no delivery guarantee applies. The call runs in the caller's own flow; nothing is
/// queued, retried or published. The counts are a reading taken while rows keep arriving and while a run may be
/// deleting, so a run that starts next can delete more or fewer rows than the preview counted.
/// </para>
/// <example>
/// <code>
/// app.MapGet("/ops/retention-preview", async (IRetentionPreviewService previews, CancellationToken cancellationToken) =&gt;
///     await previews.PreviewAsync(RetentionTargets.Sessions, cancellationToken));
/// </code>
/// </example>
/// </remarks>
public interface IRetentionPreviewService
{
    /// <summary>Previews what a retention run would delete for the current tenant.</summary>
    /// <param name="target">
    /// The one target to preview (see <see cref="RetentionTargets"/>), or <see langword="null"/> for every target.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One entry per previewed target, in the order of <see cref="RetentionTargets.All"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="target"/> is not a known target, or the current tenant is not a valid tenant identifier.
    /// </exception>
    ValueTask<IReadOnlyList<RetentionPreview>> PreviewAsync(
        string? target = null,
        CancellationToken cancellationToken = default);
}
