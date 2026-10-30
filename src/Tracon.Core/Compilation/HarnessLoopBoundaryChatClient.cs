using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// The seam between Tracon's model pipeline and the layers MAF's harness puts
/// above it: each streamed update leaves as a copy, and an approval request
/// that is already answered does not enter Tracon's tool-call loop.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline under a harness agent ends in Tracon's own tool-call loop
/// (<c>FunctionInvokingChatClient</c>), and that loop keeps every update it
/// yields so it can rebuild the turn once the stream ends. MAF's
/// <c>PerServiceCallChatHistoryPersistingChatClient</c>, right above this
/// seam, writes a sentinel <c>ConversationId</c> onto every update it
/// receives. Handed the loop's own instance, that write reached the loop's
/// copy of the turn: the loop took the conversation for one the service
/// stores, and sent the next call only the tool result. Measured on the
/// OpenAI Chat Completions surface: <c>400 messages.[1].role</c> on the first
/// tool call of every streamed harness run. A
/// non-streamed run was not affected: MAF writes onto the finished response,
/// after the loop is done with it.
/// </para>
/// <para>
/// The copy is shallow — the same contents, a new update object — which is all
/// the sentinel write needs.
/// </para>
/// <para>
/// The same MAF layer loads the session's history on every model call. On the
/// turn that answers a pending approval, MAF's own tool-call loop above it runs
/// the approved tool and removes the approval response from the turn's
/// messages, but the loaded history still holds the approval request. Tracon's
/// loop then found a request with no response and threw
/// <see cref="InvalidOperationException"/>, so every approved harness turn
/// failed. A request whose tool call already has a result is answered; this
/// seam removes it before the loop sees it. A request with no result and no
/// response is left alone.
/// </para>
/// </remarks>
// Measured with ScriptedChatCompletionsServer.
// Measured with HarnessApprovalContinuationTests.
internal sealed class HarnessLoopBoundaryChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    /// <inheritdoc />
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetResponseAsync(WithoutAnsweredApprovalRequests(messages), options, cancellationToken);

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(WithoutAnsweredApprovalRequests(messages), options, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return update.Clone();
        }
    }

    /// <summary>
    /// Removes each <see cref="ToolApprovalRequestContent"/> whose tool call
    /// already has a <see cref="FunctionResultContent"/> and no
    /// <see cref="ToolApprovalResponseContent"/> in <paramref name="messages"/>.
    /// </summary>
    /// <param name="messages">The messages of one model call.</param>
    /// <returns>
    /// <paramref name="messages"/> itself when nothing is removed; otherwise a
    /// new list. A changed message is a clone: the loaded history is never
    /// modified.
    /// </returns>
    internal static IEnumerable<ChatMessage> WithoutAnsweredApprovalRequests(IEnumerable<ChatMessage> messages)
    {
        var list = messages as IList<ChatMessage> ?? [.. messages];
        HashSet<string>? results = null;
        HashSet<string>? responses = null;
        var hasRequest = false;

        foreach (var message in list)
        {
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case FunctionResultContent result:
                        (results ??= new HashSet<string>(StringComparer.Ordinal)).Add(result.CallId);
                        break;
                    case ToolApprovalResponseContent response:
                        (responses ??= new HashSet<string>(StringComparer.Ordinal)).Add(response.ToolCall.CallId);
                        break;
                    case ToolApprovalRequestContent:
                        hasRequest = true;
                        break;
                }
            }
        }

        if (!hasRequest || results is null)
        {
            return list;
        }

        List<ChatMessage>? cleaned = null;

        for (var index = 0; index < list.Count; index++)
        {
            var message = list[index];

            if (!message.Contents.Any(content => IsAnswered(content, results, responses)))
            {
                cleaned?.Add(message);
                continue;
            }

            cleaned ??= [.. list.Take(index)];
            var kept = message.Contents.Where(content => !IsAnswered(content, results, responses)).ToList();

            if (kept.Count > 0)
            {
                var clone = message.Clone();
                clone.Contents = kept;
                cleaned.Add(clone);
            }
        }

        return cleaned ?? list;
    }

    private static bool IsAnswered(AIContent content, HashSet<string> results, HashSet<string>? responses)
        => content is ToolApprovalRequestContent request
           && results.Contains(request.ToolCall.CallId)
           && responses?.Contains(request.ToolCall.CallId) != true;
}
