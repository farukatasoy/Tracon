using System.Diagnostics;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Baglam sikistirmasindaki ozetleme cagrisi icin kullanilan
/// <see cref="IChatClient"/>'i sarar; kendi span'ini acar ve token
/// kullanimini <see cref="AgentPrismRunContext"/> uzerinden calistirmanin
/// nihai kullanimina ekler.
/// </summary>
/// <remarks>
/// Yalnizca <see cref="SummarizationCompactionStrategy"/>'nin kullandigi
/// istemciye uygulanir. Bu cagri agent'in kendi <c>AgentResponse</c>'undan
/// tamamen ayri bir yan-kanal cagrisidir; sarmalanmazsa token'lari hicbir
/// yere kaydolmaz. Precedent: <see cref="CircuitBreakingChatClient"/>.
/// </remarks>
internal sealed class CompactionUsageTrackingChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    private static readonly ActivitySource ActivitySource = new(AgentPrismDiagnostics.ActivitySourceName);

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity(AgentPrismDiagnostics.CompactHistoryActivityName);

        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        AgentPrismRunContext.Current?.ExtraUsage?.Add(response.Usage);

        if (activity is not null && response.Usage is { } usage)
        {
            activity.SetTag(AgentPrismDiagnostics.Tags.CompactionInputTokens, usage.InputTokenCount);
            activity.SetTag(AgentPrismDiagnostics.Tags.CompactionOutputTokens, usage.OutputTokenCount);
        }

        return response;
    }
}
