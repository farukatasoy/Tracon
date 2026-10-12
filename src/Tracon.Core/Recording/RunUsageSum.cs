namespace Tracon;

/// <summary>Adds two <see cref="RunUsage"/> records the one way Tracon allows.</summary>
/// <remarks>
/// The run recorder and the side-channel cost path both add usage records. One
/// helper keeps the null-preserving rule of the breakdown counters in one place
/// (a sum that is repeated by hand drifts the first time a term is added).
/// </remarks>
internal static class RunUsageSum
{
    /// <summary>Adds <paramref name="right"/> to <paramref name="left"/>.</summary>
    /// <param name="left">The first record, or <see langword="null"/>.</param>
    /// <param name="right">The second record, or <see langword="null"/>.</param>
    /// <returns>
    /// The other record when one of them is <see langword="null"/>;
    /// <see langword="null"/> when both are.
    /// </returns>
    public static RunUsage? Add(RunUsage? left, RunUsage? right)
    {
        if (right is null)
        {
            return left;
        }

        if (left is null)
        {
            return right;
        }

        return new RunUsage
        {
            InputTokens = (left.InputTokens ?? 0) + (right.InputTokens ?? 0),
            OutputTokens = (left.OutputTokens ?? 0) + (right.OutputTokens ?? 0),
            TotalTokens = (left.TotalTokens ?? 0) + (right.TotalTokens ?? 0),

            // 🚨 The breakdown fields use a NULL-PRESERVING sum, unlike the three
            // totals above. `(a ?? 0) + (b ?? 0)` would turn "neither side
            // measured this" into the claim "measured, and it was zero"; on a run
            // whose provider never reports cache usage that would report a 0%
            // cache hit rate as if it had been observed.
            CachedInputTokens = AddOrNull(left.CachedInputTokens, right.CachedInputTokens),
            ReasoningTokens = AddOrNull(left.ReasoningTokens, right.ReasoningTokens),
            AudioInputTokens = AddOrNull(left.AudioInputTokens, right.AudioInputTokens),
            AudioOutputTokens = AddOrNull(left.AudioOutputTokens, right.AudioOutputTokens),
        };
    }

    /// <summary>Adds two optional counters, staying <see langword="null"/> when NEITHER side reported one.</summary>
    private static long? AddOrNull(long? left, long? right)
        => left is null && right is null ? null : (left ?? 0) + (right ?? 0);
}
