using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// Stamps an <see cref="AgentSession"/> with, and reads back, its Tracon
/// session identity.
/// </summary>
/// <remarks>
/// <para>
/// The identity is stored in the session's <see cref="AgentSession.StateBag"/>
/// field. This field is included in the <c>SerializeSessionAsync</c> output,
/// so the identity persists together with the session, and a restored session
/// knows its own identity.
/// </para>
/// <para>
/// The reason the state is stored on the session is Microsoft Agent
/// Framework's explicit warning: the provider and wrapper objects are shared
/// across all sessions, so no session-specific information can be kept as a
/// field.
/// </para>
/// </remarks>
public static class AgentSessionIdentity
{
    /// <summary>
    /// The state key under which the session identity is stored. This value
    /// is <strong>stable</strong>; changing it makes previously saved sessions
    /// unreadable.
    /// </summary>
    public const string StateKey = "Tracon.SessionId";

    /// <summary>Writes the Tracon identity to the session.</summary>
    /// <param name="session">The session to stamp.</param>
    /// <param name="sessionId">The session identity.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="sessionId"/> is empty.</exception>
    public static void SetId(AgentSession session, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        session.StateBag.SetValue(StateKey, sessionId, TraconCoreJsonContext.Default.Options);
    }

    /// <summary>Reads the session's Tracon identity.</summary>
    /// <param name="session">The session to read.</param>
    /// <returns>The identity; <see langword="null"/> if not stamped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    public static string? GetId(AgentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.StateBag.TryGetValue<string>(StateKey, out var sessionId, TraconCoreJsonContext.Default.Options)
            ? sessionId
            : null;
    }
}
