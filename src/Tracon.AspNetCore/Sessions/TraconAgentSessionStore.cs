using Microsoft.Agents.AI;

namespace Tracon;

// MAAI001: since Microsoft Agent Framework 1.22, AgentSessionStore and
// AgentSessionStoreKey live in Microsoft.Agents.AI.Abstractions and are marked
// "evaluation purposes only". This bridge cannot hide the type it derives from.
// It is internal, so the experimental type never reaches Tracon's public API
// (a 1.x surface cannot freeze a type its owner may still change, UR-005);
// the suppression covers this file only and the endpoint that consumes the
// store (OpenAIResponsesEndpoints). Same rationale as K-020.
#pragma warning disable MAAI001

/// <summary>
/// Connects the Microsoft Agent Framework's <see cref="AgentSessionStore"/>
/// abstraction to Tracon's <see cref="AgentSessionManager"/> class.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Does not write</strong> its own persistence code; it only delegates.
/// This way, sessions are written to the same store, with the same identity
/// stamp, and with the same tenant isolation, no matter which path they come
/// through (the management API or the OpenAI-compatible endpoints).
/// </para>
/// <para>
/// A consumer replaces it by registering its own <see cref="AgentSessionStore"/>
/// before <c>MapTracon</c> runs; the consumer's registration always wins.
/// </para>
/// <para>
/// Tracon's session identity is a single string. The only partition of
/// <see cref="AgentSessionStoreKey"/> this class understands is <c>isolation</c>,
/// the one Microsoft Agent Framework's <c>IsolationKeyScopedAgentSessionStore</c>
/// adds. It is folded into the identity in the format MAF used before it
/// introduced partitions (<c>escaped-isolation-key::session-id</c>), so
/// records written under that format stay addressable. Any other partition is
/// refused: ignoring it would merge sessions the caller meant to keep apart.
/// </para>
/// </remarks>
internal sealed class TraconAgentSessionStore : AgentSessionStore
{
    private const string IsolationPartitionName = "isolation";

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
    /// Never opens a session: if no record exists, the result is
    /// <see langword="null"/> and nothing is written. The returned session is
    /// stamped with the key's identity.
    /// </remarks>
    public override async ValueTask<AgentSession?> GetSessionAsync(
        AIAgent agent,
        AgentSessionStoreKey key,
        CancellationToken cancellationToken = default)
        => await _sessions.GetExistingSessionAsync(agent, ToSessionId(key), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    /// <remarks>
    /// If no record exists, a new session is opened (not yet written). The
    /// returned session is stamped with the key's identity in both cases.
    /// </remarks>
    public override ValueTask<AgentSession> GetOrCreateSessionAsync(
        AIAgent agent,
        AgentSessionStoreKey key,
        CancellationToken cancellationToken = default)
        => _sessions.GetOrCreateSessionAsync(agent, ToSessionId(key), cancellationToken);

    /// <inheritdoc />
    public override async ValueTask SaveSessionAsync(
        AIAgent agent,
        AgentSessionStoreKey key,
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        // The session may have been opened outside this bridge. Stamping the
        // identity before saving ensures the record is written with the correct
        // key and that run records can find the session.
        AgentSessionIdentity.SetId(session, ToSessionId(key));

        await _sessions.SaveSessionAsync(agent, session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Maps a partitioned key onto Tracon's single session identity.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The key carries a partition other than <c>isolation</c>.</exception>
    private static string ToSessionId(AgentSessionStoreKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Partitions is not { Count: > 0 } partitions)
        {
            return key.SessionId;
        }

        if (partitions.Count == 1 && partitions.TryGetValue(IsolationPartitionName, out var isolationKey))
        {
            var escaped = isolationKey
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace(":", "\\:", StringComparison.Ordinal);

            return escaped + "::" + key.SessionId;
        }

        throw new NotSupportedException(
            $"The Tracon session store supports only the '{IsolationPartitionName}' partition; " +
            $"received: {string.Join(", ", partitions.Keys)}.");
    }
}
#pragma warning restore MAAI001
