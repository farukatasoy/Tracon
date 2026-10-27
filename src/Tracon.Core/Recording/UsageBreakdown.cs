using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Reads the breakdown counters of a <see cref="UsageDetails"/> and maps a whole
/// <see cref="UsageDetails"/> to a <see cref="RunUsage"/>.
/// </summary>
/// <remarks>
/// <para>
/// The class exists to hold the <c>MEAI001</c> suppression in ONE place —
/// the same pattern <c>TraconA2ABuilderExtensions</c> uses for
/// <c>AgentRunMode</c> and <c>ChatHistoryReader</c> uses for <c>MAAI001</c>.
/// <c>InputAudioTokenCount</c> and <c>OutputAudioTokenCount</c> are marked
/// "for evaluation purposes only" in Microsoft.Extensions.AI 10.11.0 (as are
/// <c>InputTextTokenCount</c> and <c>OutputTextTokenCount</c>, which are not read);
/// <c>CachedInputTokenCount</c> and <c>ReasoningTokenCount</c> are NOT, and are
/// read here only so that all four counters travel together.
/// </para>
/// <para>
/// The suppression is deliberately narrow. Tracon's whole exposure to the
/// experimental surface is the two property reads below, and they are pure
/// reads: if the members are renamed or removed in a later release the build
/// breaks here, at one place, which is exactly the signal an evaluation-only
/// API is supposed to give. Suppressing the diagnostic at project level would
/// hide that signal everywhere else too.
/// </para>
/// <para>
/// Every counter stays <see langword="null"/> when the provider did not
/// report it. Zero is a measurement, not an absence — see <see cref="RunUsage"/>.
/// </para>
/// <para>
/// The two cache-write counters are not properties of <see cref="UsageDetails"/>; a provider
/// reports them in <see cref="UsageDetails.AdditionalCounts"/> under the keys of
/// <see cref="TraconUsageCounts"/>.
/// </para>
/// <para>
/// <see cref="ToRunUsage"/> is the ONE place a <see cref="UsageDetails"/> becomes a
/// <see cref="RunUsage"/> (the run recorder, the budget ring, both judges). A mapping
/// written out by hand at each of those sites is how a counter added to
/// <see cref="RunUsage"/> reaches some of them and not the rest.
/// </para>
/// </remarks>
internal static class UsageBreakdown
{
    /// <summary>Maps the usage of one or more model calls to a <see cref="RunUsage"/>.</summary>
    /// <param name="usage">The usage record.</param>
    /// <returns>
    /// The run usage. A counter the provider did not report stays <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// The cached, reasoning, audio and cache-write counters are counted INSIDE the three
    /// totals (the Microsoft.Extensions.AI contract), so they are recorded beside them,
    /// never added to them.
    /// </remarks>
    public static RunUsage ToRunUsage(UsageDetails usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        return new RunUsage
        {
            InputTokens = usage.InputTokenCount,
            OutputTokens = usage.OutputTokenCount,
            TotalTokens = usage.TotalTokenCount,
            CachedInputTokens = CachedInputTokens(usage),
            ReasoningTokens = ReasoningTokens(usage),
            AudioInputTokens = AudioInputTokens(usage),
            AudioOutputTokens = AudioOutputTokens(usage),
            CacheWriteInputTokens = CacheWriteInputTokens(usage),
            CacheWriteLongInputTokens = CacheWriteLongInputTokens(usage),
        };
    }

    /// <summary>Gets the cached input tokens a provider reported.</summary>
    /// <param name="usage">The usage record.</param>
    /// <returns>The count, or <see langword="null"/> when it was not reported.</returns>
    public static long? CachedInputTokens(UsageDetails usage) => usage.CachedInputTokenCount;

    /// <summary>Gets the input tokens a provider wrote to its cache with the short time to live.</summary>
    /// <param name="usage">The usage record.</param>
    /// <returns>The count, or <see langword="null"/> when it was not reported.</returns>
    public static long? CacheWriteInputTokens(UsageDetails usage)
        => AdditionalCount(usage, TraconUsageCounts.CacheWriteInputTokens);

    /// <summary>Gets the input tokens a provider wrote to its cache with the long time to live.</summary>
    /// <param name="usage">The usage record.</param>
    /// <returns>The count, or <see langword="null"/> when it was not reported.</returns>
    public static long? CacheWriteLongInputTokens(UsageDetails usage)
        => AdditionalCount(usage, TraconUsageCounts.CacheWriteLongInputTokens);

    /// <summary>Gets the reasoning tokens a provider reported.</summary>
    /// <param name="usage">The usage record.</param>
    /// <returns>The count, or <see langword="null"/> when it was not reported.</returns>
    public static long? ReasoningTokens(UsageDetails usage) => usage.ReasoningTokenCount;

    /// <summary>Gets the audio input tokens a provider reported.</summary>
    /// <param name="usage">The usage record.</param>
    /// <returns>The count, or <see langword="null"/> when it was not reported.</returns>
    public static long? AudioInputTokens(UsageDetails usage)
    {
#pragma warning disable MEAI001 // Evaluation-only; the whole exposure is this read. See the class remarks.
        return usage.InputAudioTokenCount;
#pragma warning restore MEAI001
    }

    /// <summary>Gets the audio output tokens a provider reported.</summary>
    /// <param name="usage">The usage record.</param>
    /// <returns>The count, or <see langword="null"/> when it was not reported.</returns>
    public static long? AudioOutputTokens(UsageDetails usage)
    {
#pragma warning disable MEAI001 // Evaluation-only; the whole exposure is this read. See the class remarks.
        return usage.OutputAudioTokenCount;
#pragma warning restore MEAI001
    }

    private static long? AdditionalCount(UsageDetails usage, string key)
        => usage.AdditionalCounts is { } counts && counts.TryGetValue(key, out var count) ? count : null;
}
