namespace AgentPrism.Embedded;

/// <summary>
/// 🚨 <strong>DEMONSTRATION ONLY.</strong> Denies both starting a run and
/// reaching a run's resources for a tenant this host's own directory
/// (<see cref="EmbeddedTenantStore"/>) does not know about; allows every
/// session access.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AuthorizeRunAsync"/> answers <strong>two</strong> kinds of
/// question, told apart by <see cref="RunAuthorizationRequest.Access"/>:
/// <see cref="RunAccess.Start"/> asks whether a run may begin, and every other
/// value asks whether an existing run's resource may be reached — its summary
/// and events (<see cref="RunAccess.Read"/>), its cancellation, its scores,
/// its attachments, its approval requests. The resource questions carry
/// <see cref="RunAuthorizationRequest.RunId"/>; a plain start does not.
/// </para>
/// <para>
/// This sample answers both from the same rule, because the only ownership
/// record it has is the tenant directory. A real host narrows the resource
/// questions further, by comparing
/// <see cref="RunAuthorizationRequest.UserId"/> against its own record of who
/// started that run.
/// </para>
/// <para>
/// A real host answers <see cref="AuthorizeSessionAsync"/> from its own
/// per-user ownership records — this sample has none (its background jobs
/// carry a tenant and, optionally, a user, but never a session id), so
/// narrowing session access here would demonstrate a rule this sample's own
/// data cannot back up. <see cref="AuthorizeRunAsync"/> is grounded in real
/// sample state instead: <see cref="ITenantStore"/> is the same directory
/// <c>/api/tenants</c> reads.
/// </para>
/// </remarks>
internal sealed class EmbeddedRunAuthorizationHandler(ITenantStore tenants) : IRunAuthorizationHandler
{
    public async ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
        RunAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var known = await tenants.ListAsync(cancellationToken).ConfigureAwait(false);

        if (known.Any(tenant => string.Equals(tenant.Slug, request.TenantId, StringComparison.Ordinal)))
        {
            return RunAuthorizationResult.Allow();
        }

        // The denial reason reaches the caller as ProblemDetails.Detail on a
        // start, and is DISCARDED on a resource question: a denied resource
        // answers exactly as a missing one does, so that the refusal cannot
        // confirm the resource exists.
        return RunAuthorizationResult.Deny(
            request.Access == RunAccess.Start
                ? $"'{request.TenantId}' is not a tenant this host knows about."
                : $"'{request.TenantId}' may not reach run '{request.RunId}'.");
    }

    public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(
        SessionAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ValueTask<RunAuthorizationResult>(RunAuthorizationResult.Allow());
    }
}
