using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;

namespace Tracon;

/// <summary>
/// Connects the Microsoft Agent Framework's <see cref="AgentSessionStore"/>
/// abstraction to Tracon's <see cref="AgentSessionManager"/> class.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Does not write</strong> its own persistence code; it only delegates.
/// This way, sessions are written to the same store, with the same identity
/// stamp, and with the same tenant isolation, no matter which path they come
/// through (the management API, OpenAI-compatible endpoints, or MAF hosting
/// constructs).
/// </para>
/// <para>
/// This class lives in <c>Tracon.AspNetCore</c> because
/// <see cref="AgentSessionStore"/> is in the prerelease <c>Microsoft.Agents.AI.Hosting</c>
/// package, and <c>Tracon.Core</c> cannot depend on that package.
/// </para>
/// <para>
/// In multi-tenant setups, this instance can be wrapped with MAF's
/// <c>IsolationKeyScopedAgentSessionStore</c> class; to do so, the consumer
/// registers its own <see cref="AgentSessionStore"/> before calling
/// <c>MapTracon</c>.
/// </para>
/// </remarks>
public sealed class TraconAgentSessionStore : AgentSessionStore
{
    private readonly AgentSessionManager _sessions;

    /// <summary>Creates a new bridge.</summary>
    /// <param name="sessions">The session manager to delegate to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sessions"/> is <see langword="null"/>.</exception>
    public TraconAgentSessionStore(AgentSessionManager sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _sessions = sessions;
    }

    /// <inheritdoc />
    /// <remarks>
    /// If no record exists, a new session is opened. The returned session is
    /// stamped with <paramref name="sessionStoreId"/> in both cases.
    /// </remarks>
    public override ValueTask<AgentSession> GetSessionAsync(
        AIAgent agent,
        string sessionStoreId,
        CancellationToken cancellationToken = default)
        => _sessions.GetOrCreateSessionAsync(agent, sessionStoreId, cancellationToken);

    /// <inheritdoc />
    public override async ValueTask SaveSessionAsync(
        AIAgent agent,
        string sessionStoreId,
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        // The session may have been opened outside this bridge (MAF hosting
        // constructs produce their own sessions). Stamping the identity before
        // saving ensures the record is written with the correct key and that run
        // records can find the session.
        AgentSessionIdentity.SetId(session, sessionStoreId);

        await _sessions.SaveSessionAsync(agent, session, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async ValueTask DeleteSessionAsync(
        AIAgent agent,
        string sessionStoreId,
        CancellationToken cancellationToken = default)
        => await _sessions.DeleteSessionAsync(sessionStoreId, cancellationToken).ConfigureAwait(false);
}
