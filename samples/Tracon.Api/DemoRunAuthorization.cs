using Microsoft.Extensions.Logging;

namespace Tracon.Api;

/// <summary>
/// 🚨 <strong>DEMONSTRATION ONLY. NEVER USE THIS IN PRODUCTION.</strong>
/// </summary>
/// <remarks>
/// <para>
/// This handler applies one of a few fixed, hard-coded rules chosen by
/// configuration. It expresses no real policy: a production deployment writes
/// its own <see cref="IRunAuthorizationHandler"/> against its own identity
/// system and its own notion of ownership.
/// </para>
/// <para>
/// It exists for the same reason <c>DemoRoleAuthenticationHandler</c> does.
/// Tracon ships <c>AllowAllRunAuthorizationHandler</c> by default, so this
/// reference application could never SHOW a run being refused — the whole
/// authorization seam looked like it did nothing here, and the only way to
/// exercise it was to hand-edit <c>Program.cs</c> and undo the edit
/// afterwards. A temporary edit is not a mechanism: the next person has to
/// rediscover it, and nothing stops the edit from being forgotten in the tree.
/// </para>
/// <para>
/// It is off by default and turns on only with
/// <c>Tracon:Demo:RunAuthorization:Mode</c>. The identity it compares against
/// comes from <see cref="DemoRunAttributionContext"/>, which reads the
/// <c>X-Demo-User</c> request header.
/// </para>
/// <para>
/// 🚨 It LOGS every request it is handed, at <c>Information</c>. That is the
/// second half of the demonstration: the interface promises a handler is told
/// the tenant, the agent, the session, the user and the kind of access, and a
/// decision alone cannot show which of those actually arrived. A production
/// handler would not log a caller identity on every run — this one is a
/// teaching surface, and the values it prints are demonstration values.
/// </para>
/// </remarks>
internal sealed class DemoRunAuthorization(
    DemoRunAuthorizationMode mode,
    ILogger<DemoRunAuthorization> logger) : IRunAuthorizationHandler
{
    /// <summary>The configuration key that selects the mode.</summary>
    public const string ModeKey = "Tracon:Demo:RunAuthorization:Mode";

    /// <summary>The user <see cref="DemoRunAuthorizationMode.AllowUserA"/> lets through.</summary>
    public const string AllowedUser = "a";

    /// <inheritdoc />
    public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
        RunAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation(
            "Demo run authorization: mode={Mode} tenant={TenantId} agent={AgentName} "
            + "session={SessionId} user={UserId} access={Access} run={RunId}",
            mode, request.TenantId, request.AgentName, request.SessionId,
            request.UserId, request.Access, request.RunId);

        return ValueTask.FromResult(mode switch
        {
            // 🚨 Thrown, not returned: the interface documents that a handler
            // which throws denies the call (fail-closed). A case has to be able
            // to prove that promise, so one mode exists purely to break.
            DemoRunAuthorizationMode.Throw =>
                throw new InvalidOperationException(
                    "Tracon:Demo:RunAuthorization:Mode is 'throw'. This demonstration handler "
                    + "fails on purpose so the fail-closed guarantee can be observed."),

            DemoRunAuthorizationMode.DenyAll =>
                RunAuthorizationResult.Deny("The demonstration handler refuses every run."),

            DemoRunAuthorizationMode.AllowUserA =>
                string.Equals(request.UserId, AllowedUser, StringComparison.Ordinal)
                    ? RunAuthorizationResult.Allow()
                    : RunAuthorizationResult.Deny(
                        $"The demonstration handler allows only the user '{AllowedUser}'."),

            // 🚨 The one mode whose decision depends on BOTH fields at once.
            // It exists to make the interface's own promise observable: the
            // request carries the session the caller is reaching for, not only
            // who the caller is, so a handler can enforce "this session is not
            // yours" rather than refusing the whole agent.
            DemoRunAuthorizationMode.DenyForeignSession =>
                IsOwnSession(request.SessionId, request.UserId)
                    ? RunAuthorizationResult.Allow()
                    : RunAuthorizationResult.Deny(
                        "The demonstration handler refuses a session that belongs to another user."),

            // The session modes below narrow session access only; a run itself
            // stays allowed so a case can open one before testing the refusal.
            _ => RunAuthorizationResult.Allow(),
        });
    }

    /// <summary>
    /// Whether <paramref name="sessionId"/> follows the demonstration naming
    /// rule <c>session-of-{user}</c> for <paramref name="userId"/>.
    /// </summary>
    /// <remarks>
    /// A name convention stands in for a real ownership lookup here on purpose:
    /// the point is that the DECISION can read the session, not how ownership
    /// is stored. A production handler asks its own store instead — or turns on
    /// <see cref="TraconSessionOwnershipOptions"/>, which records the opening
    /// user for exactly this question.
    /// </remarks>
    private static bool IsOwnSession(string? sessionId, string? userId) =>
        string.IsNullOrEmpty(sessionId)
        || (userId is not null
            && string.Equals(sessionId, $"session-of-{userId}", StringComparison.Ordinal));

    /// <inheritdoc />
    public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(
        SessionAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation(
            "Demo session authorization: mode={Mode} tenant={TenantId} "
            + "session={SessionId} user={UserId} access={Access}",
            mode, request.TenantId, request.SessionId, request.UserId, request.Access);

        return ValueTask.FromResult(mode switch
        {
            DemoRunAuthorizationMode.Throw =>
                throw new InvalidOperationException(
                    "Tracon:Demo:RunAuthorization:Mode is 'throw'. This demonstration handler "
                    + "fails on purpose so the fail-closed guarantee can be observed."),

            DemoRunAuthorizationMode.DenyAll =>
                RunAuthorizationResult.Deny("The demonstration handler refuses every session access."),

            DemoRunAuthorizationMode.AllowUserA =>
                string.Equals(request.UserId, AllowedUser, StringComparison.Ordinal)
                    ? RunAuthorizationResult.Allow()
                    : RunAuthorizationResult.Deny(
                        $"The demonstration handler allows only the user '{AllowedUser}'."),

            DemoRunAuthorizationMode.DenySessionRead when request.Access is SessionAccess.Read =>
                RunAuthorizationResult.Deny("The demonstration handler refuses reading a session."),

            DemoRunAuthorizationMode.DenySessionList when request.Access is SessionAccess.List =>
                RunAuthorizationResult.Deny("The demonstration handler refuses listing sessions."),

            DemoRunAuthorizationMode.DenySessionDelete when request.Access is SessionAccess.Delete =>
                RunAuthorizationResult.Deny("The demonstration handler refuses deleting a session."),

            DemoRunAuthorizationMode.DenySessionBranch when request.Access is SessionAccess.Branch =>
                RunAuthorizationResult.Deny("The demonstration handler refuses branching a session."),

            DemoRunAuthorizationMode.DenySessionVoice when request.Access is SessionAccess.Voice =>
                RunAuthorizationResult.Deny("The demonstration handler refuses opening a voice conversation."),

            _ => RunAuthorizationResult.Allow(),
        });
    }
}

/// <summary>
/// The fixed rules <see cref="DemoRunAuthorization"/> can apply.
/// </summary>
/// <remarks>
/// Parsed from <c>Tracon:Demo:RunAuthorization:Mode</c>. An unrecognized value
/// stops the application at startup rather than falling back to
/// <see cref="Off"/>: a misspelled mode that silently allowed everything would
/// turn a refusal test green for the wrong reason.
/// </remarks>
internal enum DemoRunAuthorizationMode
{
    /// <summary>Not registered; Tracon's own allow-all default stays in place.</summary>
    Off = 0,

    /// <summary>Refuses every run and every session access.</summary>
    DenyAll,

    /// <summary>Allows only <see cref="DemoRunAuthorization.AllowedUser"/>.</summary>
    AllowUserA,

    /// <summary>Throws on every decision, to demonstrate the fail-closed rule.</summary>
    Throw,

    /// <summary>
    /// Allows a run only when its session follows <c>session-of-{user}</c> for
    /// the calling user; refuses another user's session.
    /// </summary>
    DenyForeignSession,

    /// <summary>Refuses <see cref="SessionAccess.Read"/> only.</summary>
    DenySessionRead,

    /// <summary>Refuses <see cref="SessionAccess.List"/> only.</summary>
    DenySessionList,

    /// <summary>Refuses <see cref="SessionAccess.Delete"/> only.</summary>
    DenySessionDelete,

    /// <summary>Refuses <see cref="SessionAccess.Branch"/> only.</summary>
    DenySessionBranch,

    /// <summary>Refuses <see cref="SessionAccess.Voice"/> only.</summary>
    DenySessionVoice,
}
