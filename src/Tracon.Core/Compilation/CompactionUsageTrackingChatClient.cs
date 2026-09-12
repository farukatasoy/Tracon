using System.Diagnostics;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Wraps the <see cref="IChatClient"/> used by the summarization call during
/// context compaction. It opens its own span and adds token usage to the final
/// run usage through <see cref="TraconRunContext"/>.
/// </summary>
/// <remarks>
/// Applies only to the client used by <see cref="SummarizationCompactionStrategy"/>.
/// This call is a side channel that is fully separate from the agent's own
/// <c>AgentResponse</c>. Without this wrapper, its tokens are not recorded. See
/// <see cref="SideChannelUsageAccumulator"/> for the other side channel that
/// shares this pattern (bounded structured-response repair).
/// Precedent: <see cref="CircuitBreakingChatClient"/>.
/// </remarks>
internal sealed class CompactionUsageTrackingChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    private static readonly ActivitySource ActivitySource = new(TraconDiagnostics.ActivitySourceName);

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity(TraconDiagnostics.CompactHistoryActivityName);

        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        TraconRunContext.Current?.ExtraUsage?.Add(response.Usage);

        if (activity is not null && response.Usage is { } usage)
        {
            activity.SetTag(TraconDiagnostics.Tags.CompactionInputTokens, usage.InputTokenCount);
            activity.SetTag(TraconDiagnostics.Tags.CompactionOutputTokens, usage.OutputTokenCount);
        }

        return response;
    }
}
