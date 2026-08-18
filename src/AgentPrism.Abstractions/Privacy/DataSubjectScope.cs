namespace AgentPrism;

/// <summary>
/// The sessions, runs, and conversations that belong to one data subject
/// (phase 64).
/// </summary>
/// <remarks>
/// Everything else erasable (attachments, voice sessions, scores, and the
/// conversation items and responses that hang off a conversation) is reached
/// FROM these three lists — the schema already ties them to a session, a run,
/// or a conversation. There is no separate list for those: adding one would
/// let a resolver hand back a set the eraser cannot actually act on.
/// </remarks>
public sealed record DataSubjectScope
{
    /// <summary>Gets the <c>sessions.id</c> values that belong to the subject.</summary>
    public required IReadOnlyList<string> SessionIds { get; init; }

    /// <summary>Gets the <c>runs.id</c> values that belong to the subject.</summary>
    public required IReadOnlyList<Guid> RunIds { get; init; }

    /// <summary>Gets the <c>conversations.id</c> values that belong to the subject.</summary>
    public required IReadOnlyList<Guid> ConversationIds { get; init; }
}
