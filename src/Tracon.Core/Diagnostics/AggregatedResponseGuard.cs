using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Drops an agent response's <see cref="AgentResponse.RawRepresentation"/>
/// when it no longer describes the response — after a wrapper summed several
/// calls into the response it returned.
/// </summary>
/// <remarks>
/// <para>
/// MAF's <c>LoopAgent</c> (and <c>ToolApprovalAgent</c>, when an
/// auto-approval re-runs the inner agent) writes the summed usage and the
/// transcript of every call onto the LAST call's response, in place. That
/// response's raw representation is still the last call's
/// <see cref="ChatResponse"/>. <see cref="OpenTelemetryAgent"/> rebuilds the
/// response it returns from the raw representation when there is one, so the
/// sum was lost on the way out: a three-iteration loop returned, and recorded,
/// one iteration's tokens. Read again on MAF 1.25.0 (2026-10-16, decompiled):
/// <c>LoopAgent</c> applies the sum to the last response in place, and
/// <see cref="OpenTelemetryAgent"/> prefers the raw representation.
/// </para>
/// <para>
/// Sits directly inside <see cref="OpenTelemetryAgent"/>
/// (<see cref="OpenTelemetryAgentDecorator"/>). With the raw representation
/// cleared, <see cref="OpenTelemetryAgent"/> returns this same response object.
/// A response whose raw representation still matches it is left alone.
/// </para>
/// </remarks>
internal sealed class AggregatedResponseGuard(AIAgent innerAgent) : DelegatingAIAgent(innerAgent)
{
    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);

        if (response.RawRepresentation is ChatResponse raw && Diverged(raw, response))
        {
            response.RawRepresentation = null;
        }

        return response;
    }

    private static bool Diverged(ChatResponse raw, AgentResponse response)
        => raw.Messages.Count != response.Messages.Count ||
           raw.Usage?.InputTokenCount != response.Usage?.InputTokenCount ||
           raw.Usage?.OutputTokenCount != response.Usage?.OutputTokenCount ||
           raw.Usage?.TotalTokenCount != response.Usage?.TotalTokenCount;
}
