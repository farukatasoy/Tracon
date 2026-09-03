namespace AgentPrism.Embedded;

/// <summary>
/// 🚨 <strong>DEMONSTRATION ONLY.</strong> Denies starting a run for a tenant
/// this host's own directory (<see cref="EmbeddedTenantStore"/>) does not
/// know about; allows every session access.
/// </summary>
/// <remarks>
/// A real host answers <see cref="AuthorizeSessionAsync"/> from its own
/// per-user ownership records — this sample has none (its background jobs
/// carry a tenant and, optionally, a user, but never a session id), so
/// narrowing session access here would demonstrate a rule this sample's own
/// data cannot back up. <see cref="AuthorizeRunAsync"/> is grounded in real
/// sample state instead: <see cref="ITenantStore"/> is the same directory
/// <c>/api/tenants</c> reads.
/// </remarks>
internal sealed class EmbeddedRunAuthorizationHandler(ITenantStore tenants) : IRunAuthorizationHandler
{
    public async ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
        RunAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var known = await tenants.ListAsync(cancellationToken).ConfigureAwait(false);

        return known.Any(tenant => string.Equals(tenant.Slug, request.TenantId, StringComparison.Ordinal))
            ? RunAuthorizationResult.Allow()
            : RunAuthorizationResult.Deny($"'{request.TenantId}' is not a tenant this host knows about.");
    }

    public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(
        SessionAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ValueTask<RunAuthorizationResult>(RunAuthorizationResult.Allow());
    }
}
