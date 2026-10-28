using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SdkTtl = Anthropic.Models.Messages.Ttl;

namespace Tracon;

/// <summary>
/// Reports how many input tokens a call wrote into Anthropic's prompt cache, split by the entry's
/// time to live, under the provider-independent keys of <see cref="TraconUsageCounts"/>.
/// </summary>
/// <remarks>
/// <para>
/// Anthropic bills a cache write by how long the entry lives (five minutes and one hour are two
/// prices) and apart from plain input. Measured (Anthropic SDK 13.3.0, the adapter decompiled 2026-10-28 and unchanged on this point since the first measure; both models and both call
/// shapes): the write tokens are INSIDE <see cref="UsageDetails.InputTokenCount"/>, and the adapter
/// reports only their total, as <c>AdditionalCounts["CacheCreationInputTokens"]</c>; the split
/// stays in the raw usage object. Non-streaming it is <c>usage.cache_creation</c> of the response's
/// <see cref="Message"/>. Streaming it is on the <c>message_start</c> event only: the
/// <c>message_delta</c> event carries the total and no split, and the adapter's own
/// <see cref="UsageContent"/> comes last, after the events, with the total alone.
/// </para>
/// <para>
/// So the split is read from the raw side and written next to the total the adapter reports. A
/// call that wrote nothing carries no <c>CacheCreationInputTokens</c> key (the adapter leaves out a
/// zero), and gets no key here either.
/// </para>
/// <para>
/// When the split is missing (an SDK or API that moves it), the whole total is written under the
/// time to live the binding asks for (<c>anthropic.promptCaching.ttl</c>), or under the short one
/// when it sets none: Tracon sends one time to live per binding, so that is the class a write
/// belongs to. The first time that happens a warning is logged. A split that adds up to less than
/// the total hands the remainder to the same class.
/// </para>
/// <para>
/// Sits directly on the adapter, inside <see cref="AnthropicStopReasonChatClient"/>: each API call
/// is read on its own, a continuation of a paused turn included, and the usages are summed by the
/// layers above.
/// </para>
/// </remarks>
internal sealed class AnthropicCacheUsageChatClient : DelegatingChatClient
{
    private const string AdapterCacheCreationCount = "CacheCreationInputTokens";

    private static readonly string LongTtl = ((ApiEnum<string, SdkTtl>)SdkTtl.Ttl1h).Raw();

    private readonly bool _fallbackIsLong;
    private readonly string _fallbackTtl;
    private readonly ILogger? _logger;
    private int _warned;

    /// <summary>Initializes the ring.</summary>
    /// <param name="inner">The Anthropic adapter.</param>
    /// <param name="configuredTtl">
    /// The <c>cache_control.ttl</c> wire value the binding sends, or <see langword="null"/> when it sets none.
    /// </param>
    /// <param name="logger">Optional logger for the missing-split warning.</param>
    public AnthropicCacheUsageChatClient(IChatClient inner, string? configuredTtl, ILogger? logger = null)
        : base(inner)
    {
        _fallbackIsLong = string.Equals(configuredTtl, LongTtl, StringComparison.Ordinal);
        _fallbackTtl = _fallbackIsLong ? LongTtl : ((ApiEnum<string, SdkTtl>)SdkTtl.Ttl5m).Raw();
        _logger = logger;
    }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        if (response.Usage is { } usage)
        {
            var breakdown = response.RawRepresentation is Message message ? ReadMessage(message) : null;

            Report(usage, breakdown);
        }

        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // One streamed message is one API call. Its split arrives on the first event and its usage
        // on the last update, so the split is held until then; a paused turn's continuation is a
        // second call through this ring and starts with its own message_start.
        CacheBreakdown? breakdown = null;

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false))
        {
            if (update.RawRepresentation is RawMessageStreamEvent streamEvent)
            {
                breakdown = ReadStreamEvent(streamEvent, breakdown);
            }

            foreach (var content in update.Contents)
            {
                if (content is UsageContent { Details: { } details })
                {
                    Report(details, breakdown);
                }
            }

            yield return update;
        }
    }

    /// <summary>Reads <c>usage.cache_creation</c> of a response message.</summary>
    private static CacheBreakdown? ReadMessage(Message message)
        => message.RawData.TryGetValue("usage", out var usage) ? ReadUsage(usage) : null;

    /// <summary>Reads the split from a <c>message_start</c> event; any other event leaves what was read.</summary>
    private static CacheBreakdown? ReadStreamEvent(RawMessageStreamEvent streamEvent, CacheBreakdown? current)
    {
        var json = streamEvent.Json;

        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String
            || !string.Equals(type.GetString(), "message_start", StringComparison.Ordinal))
        {
            return current;
        }

        return json.TryGetProperty("message", out var message)
               && message.ValueKind == JsonValueKind.Object
               && message.TryGetProperty("usage", out var usage)
            ? ReadUsage(usage)
            : null;
    }

    private static CacheBreakdown? ReadUsage(JsonElement usage)
    {
        if (usage.ValueKind != JsonValueKind.Object
            || !usage.TryGetProperty("cache_creation", out var split)
            || split.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new CacheBreakdown(Count(split, "ephemeral_5m_input_tokens"), Count(split, "ephemeral_1h_input_tokens"));
    }

    private static long Count(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt64(out var count) && count > 0 ? count : 0;

    /// <summary>Writes the two Tracon keys next to the total the adapter reported.</summary>
    private void Report(UsageDetails usage, CacheBreakdown? breakdown)
    {
        // No total, no write: the adapter leaves out a zero, and the keys follow it.
        if (usage.AdditionalCounts is not { } counts
            || !counts.TryGetValue(AdapterCacheCreationCount, out var total)
            || total <= 0)
        {
            return;
        }

        long shortTokens = 0;
        long longTokens = 0;

        if (breakdown is { } split)
        {
            shortTokens = split.Short;
            longTokens = split.Long;
        }
        else
        {
            WarnOnce();
        }

        var remainder = total - shortTokens - longTokens;

        if (remainder > 0)
        {
            if (_fallbackIsLong)
            {
                longTokens += remainder;
            }
            else
            {
                shortTokens += remainder;
            }
        }

        if (shortTokens > 0)
        {
            counts[TraconUsageCounts.CacheWriteInputTokens] = shortTokens;
        }

        if (longTokens > 0)
        {
            counts[TraconUsageCounts.CacheWriteLongInputTokens] = longTokens;
        }
    }

    private void WarnOnce()
    {
        if (_logger is null || Interlocked.Exchange(ref _warned, 1) != 0)
        {
            return;
        }

        _logger.LogWarning(
            "Anthropic reported cache-creation tokens without the per-TTL split (usage.cache_creation). " +
            "The binding's cache TTL setting decides the class they are priced in: '{Ttl}'.",
            _fallbackTtl);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct CacheBreakdown(long Short, long Long);
}
