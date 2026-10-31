namespace Tracon;

/// <summary>
/// The one rule that decides whether an untrusted session identifier may be used
/// by the calling tenant.
/// </summary>
/// <remarks>
/// <para>
/// A session identity is the tenant AND the identifier: two tenants may hold the
/// same identifier, and each one's session is a different session.
/// <see cref="ISessionStore.GetAsync"/> is scoped to the ambient tenant, so the
/// gate asks only that question. It never looks across tenants — an answer that
/// did would name an identifier the caller does not hold, confirm that another
/// tenant holds it, and, once two tenants share an identifier, refuse the
/// caller their own session.
/// </para>
/// <para>
/// An identifier the calling tenant does not hold is not refused: it is
/// unused, and the first turn opens a session under it in the caller's own
/// tenant. The refusal below is defense in depth for a custom
/// <see cref="ISessionStore"/> that ignores the tenant and hands back another
/// tenant's record; the stores Tracon ships cannot reach it.
/// </para>
/// <para>
/// The OpenAI-compatible endpoints and the voice endpoints both call this, so
/// the two surfaces cannot drift apart on the rule. Do NOT replace it with a
/// lookup by identifier alone: that is the tenant-blind question this class
/// exists to avoid asking.
/// </para>
/// </remarks>
internal static class SessionTenantGate
{
    /// <summary>Checks whether the calling tenant may use a session identifier.</summary>
    /// <param name="store">The session store.</param>
    /// <param name="sessionId">The session identifier, which is untrusted input.</param>
    /// <param name="tenantId">The calling tenant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if the calling tenant holds no session under the
    /// identifier yet, or holds one that is its own; <see langword="false"/> if
    /// the store returned a record that belongs to another tenant.
    /// </returns>
    public static async ValueTask<bool> MayUseAsync(
        ISessionStore store,
        string sessionId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var record = await store.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

        return record is null || string.Equals(record.TenantId, tenantId, StringComparison.Ordinal);
    }
}
