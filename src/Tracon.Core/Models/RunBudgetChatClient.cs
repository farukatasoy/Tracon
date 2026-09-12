using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Enforces the running run tree's <see cref="AgentRunBudget"/> between two
/// model turns, inside the tool-call loop.
/// </summary>
/// <remarks>
/// <para>
/// Sits in the same ring position as <see cref="TraconResponseCachingChatClient"/>
/// (<see cref="ModelProviderRegistry.BuildPipeline"/>): inside
/// <c>UseFunctionInvocation()</c>, outside the response cache and telemetry.
/// This way a check runs before every real model call the loop makes,
/// including the summarization call context compaction issues through the
/// same pipeline — and a blocked turn short-circuits before a cache lookup or
/// a <c>chat</c> span is produced, the same as a cache hit does today.
/// </para>
/// <para>
/// <strong>Reads <see cref="TraconRunContext"/>, never writes it.</strong>
/// The compiled agent (and therefore this decorator instance) is cached
/// across runs (<c>CompiledAgentCache</c>); the budget is per-run. The scope
/// is read fresh on every call, not captured at construction.
/// </para>
/// <para>
/// This decorator owns 100% of the tree's usage accounting: because it sees
/// every real model call the pipeline makes — the agent's own turns AND the
/// compaction side-channel call (both are built through the same
/// <see cref="IModelProviderRegistry.CreateChatClient(ModelBinding)"/> call) —
/// <c>RunRecordingAgent.CompleteAsync</c> no longer records into the budget
/// itself; doing so would double the count.
/// </para>
/// </remarks>
internal sealed class RunBudgetChatClient(
    IChatClient inner,
    string provider,
    string model,
    IRunPricingResolver? pricingResolver)
    : DelegatingChatClient(inner)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var budget = ThrowIfExhausted();

        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        Record(budget, response.Usage);

        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var budget = ThrowIfExhausted();

        UsageDetails? usage = null;

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false))
        {
            foreach (var content in update.Contents)
            {
                if (content is UsageContent usageContent)
                {
                    usage = usageContent.Details;
                }
            }

            yield return update;
        }

        Record(budget, usage);
    }

    /// <summary>
    /// Checks the ambient run's budget before letting the call reach the
    /// provider; returns the budget so the caller does not read the
    /// <see cref="AsyncLocal{T}"/>-backed scope a second time for recording.
    /// </summary>
    private static AgentRunBudget? ThrowIfExhausted()
    {
        var budget = TraconRunContext.Current?.Budget;

        if (budget is not null && budget.IsExhausted)
        {
            throw new TraconRunBudgetExceededException(budget.DescribeModelCallExhaustion());
        }

        return budget;
    }

    private void Record(AgentRunBudget? budget, UsageDetails? usage)
    {
        if (budget is null || usage is null)
        {
            return;
        }

        var tokens = usage.TotalTokenCount ?? 0;

        // The resolver is skipped entirely when no cost cap is set: it is not
        // free (a catalog scan, then a configuration lookup), and a tree with
        // no cost limit has no use for the answer.
        var cost = budget.MaxTotalCost is not null
            ? pricingResolver?.Resolve(provider, model, ToRunUsage(usage))?.Total()
            : null;

        budget.RecordUsage(tokens, cost);
    }

    private static RunUsage ToRunUsage(UsageDetails usage)
        => new()
        {
            InputTokens = usage.InputTokenCount,
            OutputTokens = usage.OutputTokenCount,
            TotalTokens = usage.TotalTokenCount,
            CachedInputTokens = UsageBreakdown.CachedInputTokens(usage),
            ReasoningTokens = UsageBreakdown.ReasoningTokens(usage),
            AudioInputTokens = UsageBreakdown.AudioInputTokens(usage),
            AudioOutputTokens = UsageBreakdown.AudioOutputTokens(usage),
        };
}
