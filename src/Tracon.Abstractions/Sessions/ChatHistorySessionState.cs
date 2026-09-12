namespace Tracon;

/// <summary>
/// The stable keys Tracon uses in an <c>AgentSession</c> state bag.
/// </summary>
/// <remarks>
/// These values are <strong>stable</strong>; changing them breaks existing
/// sessions' history.
/// </remarks>
public static class TraconSessionStateKeys
{
    /// <summary>
    /// The key the chat history provider stores the conversation identifier under.
    /// </summary>
    /// <remarks>
    /// The provider instance is <em>shared across all sessions</em> (Microsoft
    /// Agent Framework's explicit warning), so the conversation identifier is
    /// carried in the session's own state, not in the provider's field.
    /// </remarks>
    public const string ChatHistory = "Tracon.ChatHistory";
}

/// <summary>
/// The state the chat history provider stores within a session.
/// </summary>
/// <remarks>
/// The type is <strong>public</strong> because two separate layers read it:
/// the SQL provider (the writer) and conversation branching (the side that
/// links the new conversation opened by <see cref="IConversationBranchStore"/>
/// to a new session).
/// </remarks>
public sealed class ChatHistoryState
{
    /// <summary>The identifier of the conversation record holding this session's messages.</summary>
    public Guid ConversationId { get; set; }
}
