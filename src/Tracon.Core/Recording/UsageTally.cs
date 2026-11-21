using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>The thread-safe counters of one share of a run's token usage.</summary>
/// <remarks>
/// One tally per model in <see cref="SideChannelUsageAccumulator"/>, one for the whole agent model in
/// <see cref="ObservedUsageAccumulator"/>. A counter no call reported stays unreported, so a share that was
/// never measured is not recorded as a measured zero.
/// </remarks>
internal sealed class UsageTally
{
    private long _inputTokens;
    private long _outputTokens;
    private long _totalTokens;

    // 🚨 The breakdown counters carry their own "was this ever reported" flag
    // instead of leaning on "the sum is still zero". A provider that reports
    // CachedInputTokenCount = 0 has MEASURED a cache miss; one that reports
    // nothing has measured nothing, and the two must not collapse into the same
    // record (the null-not-zero rule of RunUsage).
    private long _cachedInputTokens;
    private long _reasoningTokens;
    private long _audioInputTokens;
    private long _audioOutputTokens;
    private long _cacheWriteInputTokens;
    private long _cacheWriteLongInputTokens;
    private int _cachedInputReported;
    private int _reasoningReported;
    private int _audioInputReported;
    private int _audioOutputReported;
    private int _cacheWriteReported;
    private int _cacheWriteLongReported;

    public void Add(UsageDetails? usage)
    {
        if (usage is null)
        {
            return;
        }

        if (usage.InputTokenCount is { } input)
        {
            Interlocked.Add(ref _inputTokens, input);
        }

        if (usage.OutputTokenCount is { } output)
        {
            Interlocked.Add(ref _outputTokens, output);
        }

        if (usage.TotalTokenCount is { } total)
        {
            Interlocked.Add(ref _totalTokens, total);
        }

        AddReported(UsageBreakdown.CachedInputTokens(usage), ref _cachedInputTokens, ref _cachedInputReported);
        AddReported(UsageBreakdown.ReasoningTokens(usage), ref _reasoningTokens, ref _reasoningReported);
        AddReported(UsageBreakdown.AudioInputTokens(usage), ref _audioInputTokens, ref _audioInputReported);
        AddReported(UsageBreakdown.AudioOutputTokens(usage), ref _audioOutputTokens, ref _audioOutputReported);
        AddReported(UsageBreakdown.CacheWriteInputTokens(usage), ref _cacheWriteInputTokens, ref _cacheWriteReported);
        AddReported(UsageBreakdown.CacheWriteLongInputTokens(usage), ref _cacheWriteLongInputTokens, ref _cacheWriteLongReported);
    }

    public RunUsage? ToRunUsage()
    {
        var input = Interlocked.Read(ref _inputTokens);
        var output = Interlocked.Read(ref _outputTokens);
        var total = Interlocked.Read(ref _totalTokens);

        var cachedInput = ReadReported(ref _cachedInputTokens, ref _cachedInputReported);
        var reasoning = ReadReported(ref _reasoningTokens, ref _reasoningReported);
        var audioInput = ReadReported(ref _audioInputTokens, ref _audioInputReported);
        var audioOutput = ReadReported(ref _audioOutputTokens, ref _audioOutputReported);
        var cacheWrite = ReadReported(ref _cacheWriteInputTokens, ref _cacheWriteReported);
        var cacheWriteLong = ReadReported(ref _cacheWriteLongInputTokens, ref _cacheWriteLongReported);

        if (input == 0 && output == 0 && total == 0 &&
            cachedInput is null && reasoning is null && audioInput is null && audioOutput is null &&
            cacheWrite is null && cacheWriteLong is null)
        {
            return null;
        }

        return new RunUsage
        {
            InputTokens = input == 0 ? null : input,
            OutputTokens = output == 0 ? null : output,
            TotalTokens = total == 0 ? null : total,
            CachedInputTokens = cachedInput,
            ReasoningTokens = reasoning,
            AudioInputTokens = audioInput,
            AudioOutputTokens = audioOutput,
            CacheWriteInputTokens = cacheWrite,
            CacheWriteLongInputTokens = cacheWriteLong,
        };
    }

    private static void AddReported(long? value, ref long counter, ref int reported)
    {
        if (value is not { } amount)
        {
            return;
        }

        Interlocked.Add(ref counter, amount);
        Interlocked.Exchange(ref reported, 1);
    }

    private static long? ReadReported(ref long counter, ref int reported)
        => Interlocked.CompareExchange(ref reported, 0, 0) == 0 ? null : Interlocked.Read(ref counter);
}
