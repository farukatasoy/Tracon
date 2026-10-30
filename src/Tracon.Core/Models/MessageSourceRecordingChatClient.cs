using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Writes the run's <see cref="RunEventCustomTypes.Context"/> event on the first model call of the run: the
/// request's messages, counted by the source that added them.
/// </summary>
/// <remarks>
/// <para>
/// The ring sits right under the tool-call loop and above an extension's <c>ModelCall</c> ring. It must
/// see the request the model is asked, with what a context provider added: the agent runs its providers
/// before it calls the client, so the messages are here with their source attached. Below an extension
/// ring that answers by itself the request would never reach it, and the record would be missing for
/// exactly the run that did not call a model.
/// </para>
/// <para>
/// One event for each run (<see cref="AgentRunScope.TryClaimContextRecord"/>): the later calls of the loop
/// carry the tool results, not a new request. A call the run makes outside the agent's own turns, the
/// context summarizer and a loop judge, is not the run's request: <see cref="AgentRunScope.SideChannelFallback"/>
/// is set for it and the ring leaves it alone BEFORE it claims, because the summarizer runs before the
/// agent's first call and would otherwise take the claim. A child run has a scope of its own and writes
/// its own event.
/// </para>
/// <para>
/// Observability does not break functionality: a failure to count or to write is logged and the model call goes on.
/// </para>
/// </remarks>
internal sealed class MessageSourceRecordingChatClient(IChatClient inner, ILogger? logger) : DelegatingChatClient(inner)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        messages = await RecordAsync(messages, cancellationToken).ConfigureAwait(false);

        return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        messages = await RecordAsync(messages, cancellationToken).ConfigureAwait(false);

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return update;
        }
    }

    /// <summary>Writes the event when this call is the run's first own call; returns the messages to pass on.</summary>
    private async ValueTask<IEnumerable<ChatMessage>> RecordAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken)
    {
        if (TraconRunContext.Current is not { Writer: { } writer } scope ||
            scope.SideChannelFallback is not null ||
            !scope.TryClaimContextRecord())
        {
            return messages;
        }

        // Read once: the request may be a lazy sequence, and the call below must see the same messages.
        var request = messages as IReadOnlyList<ChatMessage> ?? [.. messages];

        RunEventDraft draft;

        try
        {
            draft = ContextEventPayload.Describe(request);
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            logger?.LogWarning(
                ex,
                "Failed to count the message sources of the request for the Tracon run record. Run {RunId} continues " +
                "normally but the record has no context event.",
                writer.RunId);

            return request;
        }

        await writer.AppendReservedAsync(draft, cancellationToken).ConfigureAwait(false);

        return request;
    }
}
