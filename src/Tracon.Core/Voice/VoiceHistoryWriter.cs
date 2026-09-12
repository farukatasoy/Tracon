using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>Writes voice turns into an agent session's chat history.</summary>
/// <remarks>
/// <para>
/// This class exists so the <c>MAAI001</c> suppression lives in exactly
/// <strong>one</strong> place. There is no other public way to write to a session's
/// history — <c>StoreChatHistoryAsync</c> is protected — and a suppression copied to
/// a second caller is a suppression nobody revisits when the framework opens the
/// door properly.
/// </para>
/// <para>
/// Every write here is observability, not function: a history that cannot be
/// written is logged and the conversation continues.
/// </para>
/// </remarks>
internal sealed class VoiceHistoryWriter
{
    private readonly ChatHistoryProvider _chatHistory;
    private readonly AgentSessionManager _sessions;
    private readonly ILogger _logger;

    /// <summary>Creates a writer.</summary>
    /// <param name="chatHistory">The history provider.</param>
    /// <param name="sessions">The session manager.</param>
    /// <param name="logger">The logger.</param>
    public VoiceHistoryWriter(
        ChatHistoryProvider chatHistory,
        AgentSessionManager sessions,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(chatHistory);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(logger);

        _chatHistory = chatHistory;
        _sessions = sessions;
        _logger = logger;
    }

    /// <summary>Records that a streaming response was interrupted.</summary>
    /// <param name="agent">The agent.</param>
    /// <param name="session">The session.</param>
    /// <param name="partial">What had been said before the interruption.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// This step cannot be skipped. When a streaming run is cancelled the Microsoft
    /// Agent Framework does not write the history; on the next turn the model does not
    /// see its own half sentence, and the conversation breaks the moment the user says
    /// "what you said a moment ago". The record states <strong>explicitly</strong>
    /// that the answer was interrupted.
    /// </remarks>
    public async Task RecordInterruptionAsync(AIAgent agent, AgentSession session, string partial)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(session);

        var trimmed = (partial ?? string.Empty).Trim();

        var text = trimmed.Length > 0
            ? trimmed + "\n\n[The response was interrupted by the user.]"
            : "[The response was interrupted by the user before it started.]";

        await WriteAsync(agent, session, [new ChatMessage(ChatRole.Assistant, text)], "interrupted response")
            .ConfigureAwait(false);
    }

    /// <summary>Records one turn of a conversation.</summary>
    /// <param name="agent">The agent.</param>
    /// <param name="session">The session.</param>
    /// <param name="messages">The messages to append, in order.</param>
    /// <returns>The completion task.</returns>
    public async Task RecordTurnAsync(
        AIAgent agent,
        AgentSession session,
        IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(messages);

        if (messages.Count == 0)
        {
            return;
        }

        await WriteAsync(agent, session, messages, "conversation turn").ConfigureAwait(false);
    }

    private async Task WriteAsync(
        AIAgent agent,
        AgentSession session,
        IReadOnlyList<ChatMessage> messages,
        string what)
    {
        try
        {
            // The MAAI001 rationale is the same as in ChatHistoryReader: there is no
            // other public way to write to the history (StoreChatHistoryAsync is
            // protected).
#pragma warning disable MAAI001
            var context = new ChatHistoryProvider.InvokedContext(agent, session, [], messages);
#pragma warning restore MAAI001

            await _chatHistory.InvokedAsync(context, CancellationToken.None).ConfigureAwait(false);

            await _sessions.SaveSessionAsync(agent, session, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TraconException or InvalidOperationException or NotSupportedException or JsonException)
        {
            // Observability does not break functionality: when the history cannot be
            // written the conversation still continues.
            _logger.LogWarning(exception, "The {What} could not be written to the session history.", what);
        }
    }
}
