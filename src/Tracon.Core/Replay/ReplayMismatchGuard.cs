using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Converts an unmatched tool call to a real error after the run completes.
/// </summary>
/// <remarks>
/// <para>
/// This wrapper exists because <c>FunctionInvokingChatClient</c> <strong>swallows</strong>
/// exceptions from a tool body. This was measured. An error thrown from the playback
/// does not reach the endpoint and the request returns <c>200</c>. Playback records
/// the mismatch, ends the loop, and this wrapper throws the error outside the model call.
/// </para>
/// <para>
/// The wrapper is <strong>inside</strong> <see cref="RunRecordingAgent"/>, so the
/// exception reaches the recording wrapper's <c>catch</c> block, the <c>runs</c> row
/// ends as <c>Failed</c>, and the error has deterministic
/// <see cref="ReplayToolMismatchException.ReplayToolMismatchErrorType"/>. In the
/// reverse order, the run would appear Completed and its record would be silently wrong.
/// </para>
/// </remarks>
internal sealed class ReplayMismatchGuard(AIAgent innerAgent, RecordedToolPlayback playback)
    : DelegatingAIAgent(innerAgent)
{
    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);

        playback.ThrowIfMismatched();

        return response;
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base
            .RunCoreStreamingAsync(messages, session, options, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return update;
        }

        playback.ThrowIfMismatched();
    }
}
