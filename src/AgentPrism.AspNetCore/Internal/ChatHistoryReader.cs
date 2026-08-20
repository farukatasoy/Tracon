using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Reads the chat history of a stored session.
/// </summary>
/// <remarks>
/// <para>
/// The history is read through the registered <see cref="ChatHistoryProvider"/>.
/// <c>InvokingAsync</c> and the <c>InvokingContext</c> constructor are part of the
/// Microsoft Agent Framework's public surface; the provider only reads in this call,
/// it writes nothing. The same path works for both the in-memory and the PostgreSQL
/// setup (<c>AddAgentPrism</c> registers the provider explicitly).
/// </para>
/// <para>
/// Both the management API (<c>/api/sessions/{id}</c>) and the OpenAI-compatible
/// Conversations endpoint (<c>/v1/conversations/{id}/items</c>) read from here; the
/// two endpoints must show the same truth.
/// </para>
/// </remarks>
internal static class ChatHistoryReader
{
    /// <summary>Reads the chat history of a session record.</summary>
    /// <param name="record">The session record.</param>
    /// <param name="catalog">The agent catalog.</param>
    /// <param name="chatHistory">The registered chat history provider.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The messages; <see langword="null"/> if the history could not be read.
    /// </returns>
    /// <remarks>
    /// Failing to read the history is <strong>not</strong> an error: the agent may
    /// have been deleted, its provider removed, or the MAF serialization format may
    /// have changed. Observability does not break functionality; the caller still
    /// returns the metadata.
    /// </remarks>
    public static async ValueTask<IReadOnlyList<ChatMessage>?> ReadAsync(
        SessionRecord record,
        IAgentCatalog catalog,
        ChatHistoryProvider chatHistory,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var agent = await catalog.ResolveAsync(record.AgentName, culture: null, cancellationToken).ConfigureAwait(false);

            if (agent is null)
            {
                return null;
            }

            var session = await agent
                .DeserializeSessionAsync(record.State, jsonSerializerOptions: null, cancellationToken)
                .ConfigureAwait(false);

            return await ReadAsync(agent, session, chatHistory, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AgentPrismException or System.Text.Json.JsonException or InvalidOperationException or NotSupportedException)
        {
            loggerFactory
                .CreateLogger(typeof(ChatHistoryReader).FullName!)
                .LogWarning(
                    ex,
                    "Could not read the chat history of session '{SessionId}'. Returning metadata without history.",
                    record.Id);

            return null;
        }
    }

    /// <summary>
    /// Reads the chat history of a resolved agent and an open session.
    /// </summary>
    /// <param name="agent">The resolved agent.</param>
    /// <param name="session">The open session.</param>
    /// <param name="chatHistory">The registered chat history provider.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The messages.</returns>
    /// <remarks>
    /// This path is also used to find pending tool approval requests: producing an
    /// approval response requires the REQUEST ITSELF
    /// (<c>ToolApprovalRequestContent.CreateResponse</c>), and the request lives
    /// only in the session history.
    /// </remarks>
    public static async ValueTask<IReadOnlyList<ChatMessage>> ReadAsync(
        AIAgent agent,
        AgentSession session,
        ChatHistoryProvider chatHistory,
        CancellationToken cancellationToken)
    {
        // MAAI001: the InvokingContext constructor is marked "for evaluation purposes only"
        // and breaks the build under TreatWarningsAsErrors. The suppression is deliberate
        // and lives in EXACTLY ONE PLACE: there is no other public way to read the
        // history (ProvideChatHistoryAsync is protected), and this call only reads.
        // If MAF changes this API, only this spot needs updating.
        // Rationale: docs/KARARLAR.md, decision K-037.
#pragma warning disable MAAI001
        var context = new ChatHistoryProvider.InvokingContext(agent, session, []);
#pragma warning restore MAAI001

        var messages = await chatHistory.InvokingAsync(context, cancellationToken).ConfigureAwait(false);

        return [.. messages];
    }
}
