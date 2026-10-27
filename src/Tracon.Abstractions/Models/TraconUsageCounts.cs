using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// The keys of <see cref="UsageDetails.AdditionalCounts"/> that Tracon reads beyond the counters
/// <see cref="UsageDetails"/> defines.
/// </summary>
/// <remarks>
/// <para>
/// A model provider reports a prompt-cache write the way it reports any other count: it adds the
/// value to <see cref="UsageDetails.AdditionalCounts"/> under one of these keys. Tracon reads
/// them in one place, whatever the provider, so every provider that reports a cache write gets
/// the same pricing. <see cref="UsageDetails"/> has a counter for cache READS
/// (<see cref="UsageDetails.CachedInputTokenCount"/>) and none for writes.
/// </para>
/// <para>
/// A count under either key is counted INSIDE <see cref="UsageDetails.InputTokenCount"/>, like
/// the cached input tokens: the provider's input total already contains the written tokens, and
/// adding them to it counts them twice. Leave the key out for a call that wrote nothing.
/// </para>
/// <para>
/// The two keys exist because a provider can bill a write by how long the cache entry lives.
/// A provider with one time to live uses <see cref="CacheWriteInputTokens"/> alone.
/// <c>Tracon.Anthropic</c> reports both, from the per-class counts of Anthropic's usage object.
/// </para>
/// </remarks>
public static class TraconUsageCounts
{
    /// <summary>
    /// The key of the input tokens written into the provider's prompt cache with the SHORT time
    /// to live (Anthropic: five minutes).
    /// </summary>
    public const string CacheWriteInputTokens = "Tracon.CacheWriteInputTokens";

    /// <summary>
    /// The key of the input tokens written into the provider's prompt cache with the LONG time
    /// to live (Anthropic: one hour).
    /// </summary>
    public const string CacheWriteLongInputTokens = "Tracon.CacheWriteLongInputTokens";
}
