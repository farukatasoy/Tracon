using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Reports the hosted tool calls a model response carries to the run: their cost to the run's non-token
/// total and the tree's budget, and their registered names to the run recorder.
/// </summary>
/// <remarks>
/// <para>
/// A provider runs a hosted tool inside the model call, so no function invocation and no tool wrapper ever
/// sees it and <see cref="TraconToolUsage.Report"/> has no call to attach to. This ring reads the calls from
/// the response itself, at the one place that sees every real model call: inside the tool-call loop, so the
/// budget ring that guards the NEXT turn already knows what this turn spent, and inside the response cache,
/// so a cache hit that replays a response with a search in it is not billed again.
/// </para>
/// <para>
/// The unit is the call content: one <see cref="WebSearchToolCallContent"/>, one code interpreter call, one
/// file search call. The price is <c>Tracon:Pricing:HostedTools:{provider}:{kind}:PerCall</c>; a kind with no price
/// is reported with no amount, which the run's total counts as an item it does not cover and never prices at
/// zero. A call is reported when its content arrives, in a streamed response too, so the amount reaches the
/// budget before the stream ends.
/// </para>
/// <para>
/// Anthropic also states how many searches it billed (<c>WebSearchRequests</c>), and that count is the bill. In a
/// buffered response it is known first, so a search beyond it is listed at no cost. In a streamed response it
/// arrives after the calls: a higher count adds the difference as an item of its own, and a lower one leaves the
/// calls already reported (a counter never takes an amount back) and logs the difference.
/// </para>
/// <para>
/// Observability does not break functionality: nothing here throws into the model call.
/// </para>
/// </remarks>
internal sealed class HostedToolUsageChatClient(
    IChatClient inner,
    string provider,
    IOptionsMonitor<TraconOptions>? options,
    ILogger? logger)
    : DelegatingChatClient(inner)
{
    private const string AnthropicWebSearchCount = "WebSearchRequests";

    private readonly HashSet<HostedToolKind> _warnedUnpriced = [];

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        // The whole response is here, so the provider's own count is known before the first call is priced.
        var tally = new ResponseTally { BilledWebSearches = BilledWebSearches(response.Usage) };

        foreach (var message in response.Messages)
        {
            Observe(options, message.Contents, tally);
        }

        CrossCheck(tally, response.Usage);

        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var tally = new ResponseTally();
        UsageDetails? usage = null;

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false))
        {
            Observe(options, update.Contents, tally);

            foreach (var content in update.Contents)
            {
                if (content is UsageContent usageContent)
                {
                    // Summed, as the budget ring does: a response may report its usage in more than one part.
                    (usage ??= new UsageDetails()).Add(usageContent.Details);
                }
            }

            yield return update;
        }

        CrossCheck(tally, usage);
    }

    private void Observe(ChatOptions? callOptions, IEnumerable<AIContent> contents, ResponseTally tally)
    {
        if (TraconRunContext.Current is not { } scope)
        {
            return;
        }

        var fileSearchCallIds = HostedToolContent.FileSearchCallIds(contents);

        foreach (var content in contents)
        {
            if (!HostedToolContent.TryGetCall(content, fileSearchCallIds, out var kind, out var callId) || !tally.Seen.Add(callId))
            {
                continue;
            }

            // A search the provider did not bill (one that failed, say) is listed and costs nothing; the provider's
            // count is the bill. A streamed response states the count after its calls, so only a buffered one gets here.
            var billed = kind != HostedToolKind.WebSearch
                || tally.BilledWebSearches is not { } limit
                || tally.CountOf(kind) < limit;

            tally.Count(kind);

            try
            {
                Report(scope, callOptions, kind, callId, quantity: 1, billed);
            }

            // The accounting is synchronous and holds no token: no cancellation of ours can reach this catch, and
            // observability must never throw into the model call.
            catch (Exception ex)
            {
                logger?.LogWarning(
                    ex,
                    "Failed to report a {Kind} call of provider {Provider} to the run. The run continues; the call is not counted.",
                    kind,
                    provider);
            }
        }
    }

    private void Report(AgentRunScope scope, ChatOptions? callOptions, HostedToolKind kind, string callId, decimal quantity, bool billed = true)
    {
        // Only a registered tool has a record to attach the measurement to; the harness's own web search
        // counts toward the total all the same.
        var registeredName = HostedToolIdentity.FindName(callOptions, kind);

        if (registeredName is not null)
        {
            scope.HostedCalls?.Record(callId, registeredName);
        }

        var perCall = billed ? HostedToolPricing.PerCall(options?.CurrentValue.Pricing, provider, kind) : 0m;

        if (perCall is null)
        {
            WarnUnpriced(kind);
        }

        var usage = new ToolCallUsage
        {
            Unit = ToolUsageUnits.Calls,
            Quantity = quantity,
            Cost = perCall * quantity,

            // A code interpreter is billed by container session, and the response names no container: a
            // price per call is an estimate of that. Every other kind is billed by the call.
            IsEstimated = perCall is not null && kind == HostedToolKind.CodeInterpreter,
            Provider = provider,
        };

        if (NonTokenCostAccumulator.Validate(usage) is not null)
        {
            return;
        }

        if (registeredName is not null)
        {
            scope.ToolUsage?.Report(callId, usage);
        }

        scope.NonTokenCost?.ReportToolMeasurement(callId, usage);
    }

    private static long? BilledWebSearches(UsageDetails? usage)
        => usage?.AdditionalCounts is { } counts && counts.TryGetValue(AnthropicWebSearchCount, out var billed) ? billed : null;

    private void CrossCheck(ResponseTally tally, UsageDetails? usage)
    {
        if (usage?.AdditionalCounts is not { } counts
            || !counts.TryGetValue(AnthropicWebSearchCount, out var billed))
        {
            return;
        }

        var carried = tally.CountOf(HostedToolKind.WebSearch);

        if (billed == carried)
        {
            return;
        }

        logger?.LogWarning(
            "Provider {Provider} billed {Billed} web searches, and the response carries {Carried} web search calls.",
            provider,
            billed,
            carried);

        if (billed < carried || TraconRunContext.Current is not { } scope)
        {
            return;
        }

        try
        {
            // The searches the response did not show. No call identity exists for them, so the item has no
            // record of its own and counts toward the total only.
            Report(scope, callOptions: null, HostedToolKind.WebSearch, $"{provider}:web_search:unattributed:{Guid.NewGuid():N}", billed - carried);
        }

        // Same reason as in Observe: synchronous accounting, no token, never an exception into the model call.
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to report the web searches provider {Provider} billed beyond the response's calls.", provider);
        }
    }

    // A price that is missing is stated once per ring: the ring lives as long as the compiled agent.
    private void WarnUnpriced(HostedToolKind kind)
    {
        bool first;

        lock (_warnedUnpriced)
        {
            first = _warnedUnpriced.Add(kind);
        }

        if (first)
        {
            logger?.LogWarning(
                "A {Kind} call ran at provider {Provider}, and Tracon:Pricing:HostedTools:{Provider}:{Kind}:PerCall is not set. " +
                "The call is listed on the run and its cost is not added to the run's total, the budget or the quota.",
                kind,
                provider,
                provider,
                kind);
        }
    }

    // What one model response carried: which call identifiers were already counted (a streamed call can be
    // seen again), and how many of each kind.
    private sealed class ResponseTally
    {
        private readonly Dictionary<HostedToolKind, int> _counts = [];

        public HashSet<string> Seen { get; } = new(StringComparer.Ordinal);

        // The searches the provider says it billed, when it says so before the calls are read.
        public long? BilledWebSearches { get; init; }

        public void Count(HostedToolKind kind) => _counts[kind] = CountOf(kind) + 1;

        public int CountOf(HostedToolKind kind) => _counts.GetValueOrDefault(kind);
    }
}

/// <summary>Reads the configured price of a hosted tool kind.</summary>
internal static class HostedToolPricing
{
    /// <summary>Finds the price of one call.</summary>
    /// <param name="pricing">The pricing options, or <see langword="null"/> when none are available.</param>
    /// <param name="provider">The provider that ran the call.</param>
    /// <param name="kind">The kind of tool.</param>
    /// <returns>The price, or <see langword="null"/> when none is configured.</returns>
    public static decimal? PerCall(TraconPricingOptions? pricing, string provider, HostedToolKind kind)
        => pricing is not null
            && pricing.HostedTools.TryGetValue(provider, out var kinds)
            && kinds.TryGetValue(kind.ToString(), out var price)
                ? price.PerCall
                : null;
}
