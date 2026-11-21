namespace Tracon;

/// <summary>What the session ownership rule decides about a run that names a session.</summary>
internal enum SessionOwnershipVerdict
{
    /// <summary>The run may use the session, or no ownership rule applies.</summary>
    Allowed,

    /// <summary>Ownership is on and no identity could be resolved to own a session that would be opened.</summary>
    OwnerRequired,

    /// <summary>The session belongs to another user, or to nobody while unowned sessions are refused.</summary>
    NotAuthorized,
}

/// <summary>
/// The decisions every surface that starts a run makes before it does: the HTTP endpoints render
/// them as responses, <see cref="RunAdmission"/> returns them to a caller that runs an agent in process.
/// One copy, so the two cannot drift.
/// </summary>
internal static class RunAdmissionRules
{
    /// <summary>The detail given when the run authorization handler threw and the run was denied.</summary>
    internal const string FailedCheckDetail = "The run authorization check failed. Retry the request.";

    /// <summary>The detail given when the handler denies a run without a reason of its own.</summary>
    internal const string HandlerDeniedDetail = "The registered IRunAuthorizationHandler denied this run.";

    /// <summary>The detail given when the tenant's quota is spent and the quota gave no reason.</summary>
    internal const string QuotaExceededDetail = "The quota defined for this tenant has been exceeded.";

    /// <summary>
    /// The one detail "not yours" and "nobody's" answer with. Wording that told the two apart would let a caller
    /// learn, from a response it may already see, which sessions predate ownership.
    /// </summary>
    internal const string SessionNotAuthorizedDetail = "The session named in this request belongs to another user.";

    /// <summary>Builds the detail of a refusal because no identity could own a session that would be opened.</summary>
    /// <param name="sessionId">The session.</param>
    /// <returns>The detail text.</returns>
    internal static string SessionOwnerRequiredDetail(string sessionId)
        => $"Session '{sessionId}' cannot be opened: session ownership is on and no authenticated " +
            "identity could be resolved to own it.";

    /// <summary>
    /// Asks the run authorization handler whether a run may start. A handler that throws denies the run
    /// (fail closed). Only the caller's own cancellation travels out: a handler that calls its policy store and
    /// times out is an ordinary failure.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="request">What the run would do.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The handler's answer, or the failure that denied the run.</returns>
    internal static async ValueTask<HandlerOutcome> AuthorizeStartAsync(
        IRunAuthorizationHandler handler,
        RunAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var result = await handler.AuthorizeRunAsync(request, cancellationToken).ConfigureAwait(false);

            return result.IsAllowed
                ? new HandlerOutcome(true, null, null)
                : new HandlerOutcome(false, result.Reason, null);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            return new HandlerOutcome(false, null, exception);
        }
    }

    /// <summary>
    /// Decides whether a run may continue the session it names, under the session ownership rule.
    /// </summary>
    /// <param name="settings">The ownership options. A rule that is off or absent allows everything.</param>
    /// <param name="store">The session store.</param>
    /// <param name="sessionId">The session the run names, or <see langword="null"/> for a session-less run.</param>
    /// <param name="readUserId">
    /// Reads the calling user. Called only when the rule applies, because reading it asks the host's identity code.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The verdict.</returns>
    /// <remarks>
    /// A refusal here confirms that the named session exists: a fresh id proceeds, an owned one does not. That
    /// is unavoidable on a surface that must still be able to open a session. A row that has no owner is
    /// reachable by default and refused when the deployment asks for it. There is no management exemption:
    /// starting a run appends to the conversation, and continuing somebody's conversation as oneself is not the
    /// same act as reading it.
    /// </remarks>
    internal static async ValueTask<SessionOwnershipVerdict> CheckSessionAsync(
        TraconSessionOwnershipOptions? settings,
        ISessionStore store,
        string? sessionId,
        Func<string?> readUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(readUserId);

        if (settings is not { Enabled: true } || string.IsNullOrWhiteSpace(sessionId))
        {
            return SessionOwnershipVerdict.Allowed;
        }

        var record = await store.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var userId = readUserId();

        if (record is null)
        {
            // The run would OPEN this session. Refusing an unidentified caller here is the decision the session
            // manager makes at the first write; making it now means the model is never called for a turn whose
            // session cannot be kept.
            return settings.RequireAuthenticatedOwner && userId is null
                ? SessionOwnershipVerdict.OwnerRequired
                : SessionOwnershipVerdict.Allowed;
        }

        if (record.OwnerId is not { } ownerId)
        {
            return settings.RefuseUnownedSessions
                ? SessionOwnershipVerdict.NotAuthorized
                : SessionOwnershipVerdict.Allowed;
        }

        return string.Equals(ownerId, userId, StringComparison.Ordinal)
            ? SessionOwnershipVerdict.Allowed
            : SessionOwnershipVerdict.NotAuthorized;
    }

    /// <summary>The run authorization handler's answer, or the failure that denied the run.</summary>
    /// <param name="IsAllowed">Whether the run may start.</param>
    /// <param name="Reason">The handler's reason for a denial, if it gave one.</param>
    /// <param name="Failure">The exception the handler threw, if it threw.</param>
    internal readonly record struct HandlerOutcome(bool IsAllowed, string? Reason, Exception? Failure);
}
