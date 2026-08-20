using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Reads the four breakdown counters of a <see cref="UsageDetails"/>.
/// </summary>
/// <remarks>
/// <para>
/// The class exists to hold the <c>MEAI001</c> suppression in ONE place —
/// the same pattern <c>AgentPrismA2ABuilderExtensions</c> uses for
/// <c>AgentRunMode</c> and <c>ChatHistoryReader</c> uses for <c>MAAI001</c>.
/// <c>InputAudioTokenCount</c> and <c>OutputAudioTokenCount</c> are marked
/// "for evaluation purposes only" in Microsoft.Extensions.AI 10.8.3;
/// <c>CachedInputTokenCount</c> and <c>ReasoningTokenCount</c> are NOT, and are
/// read here only so that all four counters travel together.
/// </para>
/// <para>
/// The suppression is deliberately narrow. AgentPrism's whole exposure to the
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
/// </remarks>
internal static class UsageBreakdown
{
    /// <summary>Gets the cached input tokens a provider reported.</summary>
    /// <param name="usage">The usage record.</param>
    /// <returns>The count, or <see langword="null"/> when it was not reported.</returns>
    public static long? CachedInputTokens(UsageDetails usage) => usage.CachedInputTokenCount;

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
}
