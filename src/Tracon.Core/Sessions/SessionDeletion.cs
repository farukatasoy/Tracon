namespace Tracon;

/// <summary>
/// The one path that deletes a session on behalf of a caller: the session record, the copy
/// of its content that conversation memory keeps, and its attachments.
/// </summary>
/// <remarks>
/// <para>
/// Three callers share it: <c>DELETE /api/sessions/{id}</c>, <c>DELETE /v1/conversations/{id}</c>
/// and the bridge Microsoft Agent Framework 1.26 asks for (<c>AgentSessionStore.DeleteSessionAsync</c>).
/// One path keeps the three from drifting apart. When each wrote its own sequence, the conversation
/// endpoint left the attachments behind: the session was gone, so nothing could name them any more.
/// </para>
/// <para>
/// <strong>The gates stay at the HTTP edge.</strong> Run authorization and session ownership
/// are asked by the endpoints, before this class is called, for the reason
/// <see cref="AgentSessionManager"/> already gives: background work reaches the manager with no
/// caller to compare against a row. This class trusts its caller. It is <see langword="internal"/>
/// so that no consumer can reach it around a gate.
/// </para>
/// <para>
/// <strong>The tenant boundary is in the stores.</strong> The session store filters by the
/// ambient tenant, and the attachment delete takes that tenant as an argument; a session
/// of another tenant that shares the identity is never touched.
/// </para>
/// <para>
/// <strong>Order: copy, attachments, record.</strong> A step that fails leaves the record in
/// place, so a repeat call finds the session and finishes. Deleting the record first would
/// leave bytes that belong to a session nobody can name. A session that does not exist has
/// no side effect: its attachments (uploaded before a session was ever opened) stay.
/// </para>
/// </remarks>
internal sealed class SessionDeletion
{
    private readonly AgentSessionManager _sessions;
    private readonly IAttachmentStore _attachments;
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates the deletion path.</summary>
    /// <param name="sessions">Deletes the record and the copy of its content.</param>
    /// <param name="attachments">Deletes the attachments of the session.</param>
    /// <param name="tenantContext">The tenant the attachments are deleted in.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public SessionDeletion(AgentSessionManager sessions, IAttachmentStore attachments, ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _sessions = sessions;
        _attachments = attachments;
        _tenantContext = tenantContext;
    }

    /// <summary>Deletes a session of the current tenant, with everything that belongs to it.</summary>
    /// <param name="sessionId">The session identity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if a session was deleted; <see langword="false"/> if there was none.</returns>
    /// <exception cref="ArgumentException"><paramref name="sessionId"/> is empty.</exception>
    public ValueTask<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        // Read once, when the call starts: the attachments go in the tenant the call was made in.
        var tenantId = _tenantContext.TenantId;

        return _sessions.DeleteSessionAsync(
            sessionId,
            async token => await _attachments.DeleteBySessionAsync(tenantId, sessionId, token).ConfigureAwait(false),
            cancellationToken);
    }
}
