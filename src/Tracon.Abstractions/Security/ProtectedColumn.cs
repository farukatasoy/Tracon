namespace Tracon;

/// <summary>A database column <see cref="IContentProtector"/> can be applied to.</summary>
/// <remarks>
/// The list is closed and named by column, not by table: <c>payload</c>
/// appears in four different tables and only one of them (<see cref="RunEventPayload"/>)
/// carries user content — the rest is control-plane data and is never in scope.
/// </remarks>
public enum ProtectedColumn
{
    /// <summary>The <c>sessions.state</c> column — Microsoft Agent Framework's serialized session state.</summary>
    SessionState = 0,

    /// <summary>The <c>conversation_items.item</c> column — one stored chat message.</summary>
    ConversationItem = 1,

    /// <summary>The <c>run_inputs.messages</c> column — the messages a run was started with.</summary>
    RunInput = 2,

    /// <summary>The <c>run_events.text</c> column — a run event's free-text content.</summary>
    RunEventText = 3,

    /// <summary>The <c>run_events.payload</c> column — a run event's structured payload.</summary>
    RunEventPayload = 4,

    /// <summary>The <c>tool_invocations.arguments</c> column.</summary>
    ToolArguments = 5,

    /// <summary>The <c>tool_invocations.result</c> column.</summary>
    ToolResult = 6,

    /// <summary>The <c>agent_files.content</c> column.</summary>
    AgentFileContent = 7,

    /// <summary>The <c>attachments.content</c> column.</summary>
    AttachmentContent = 8,

    /// <summary>
    /// The <c>responses.payload</c> column. No store writes to this table
    /// today; it is kept in scope so the table starts protected if it is
    /// ever filled in.
    /// </summary>
    ResponsePayload = 9,
}
