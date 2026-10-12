using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// The seam between Tracon's model pipeline and the layers MAF's harness puts
/// above it: each streamed update leaves as a copy.
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
/// </remarks>
// F-315 (2026-10-12, K-892): measured with ScriptedChatCompletionsServer.
internal sealed class HarnessLoopBoundaryChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update.Clone();
        }
    }
}
