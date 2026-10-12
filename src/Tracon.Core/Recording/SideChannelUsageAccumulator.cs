using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Accumulates token usage produced by a model call that runs OUTSIDE the
/// agent's normal request/response flow during a run.
/// </summary>
/// <remarks>
/// <para>
/// Four sources feed it today. Two of them call ANOTHER model through their own
/// <see cref="IChatClient"/>, fully separate from the agent's
/// <c>AgentResponse</c>: the context compaction summarizer and the
/// <c>aiJudge</c> loop criterion's judge (both through
/// <see cref="SideChannelUsageTrackingChatClient"/>). Two of them spend on the
/// agent's OWN model but throw the response away: a bounded structured-response
/// repair turn that is superseded or never returned
/// (<see cref="StructuredResponseValidatingAgent"/>), and a harness loop that
/// fails its run and so loses the response that would have carried every
/// iteration's usage (<see cref="RecordingLoopEvaluator"/>). None of these
/// tokens enter the normal flow on their own, so this accumulator is available
/// through <see cref="TraconRunContext"/>.
/// </para>
/// <para>
/// The usage is kept PER MODEL. <see cref="Add(UsageDetails?)"/>
/// collects the agent's own model share; <see cref="Add(UsageDetails?, string, string)"/>
/// collects another model's share under its own provider and model. At run
/// completion, <see cref="RunRecordingAgent"/> adds every share to the run's
/// <see cref="RunUsage"/>, but prices each share at the model that spent it
/// (<see cref="RunCostCombiner"/>). One bucket for all of them priced a judge's
/// or a summarizer's tokens at the agent's rate.
/// </para>
/// <para>
/// The tree budget does NOT read this accumulator: <see cref="RunBudgetChatClient"/>
/// sits in every pipeline these calls go through and already charged them.
/// </para>
/// </remarks>
internal sealed class SideChannelUsageAccumulator
{
    private readonly UsageTally _agentModel = new();

    // Created on the first call to another model: most runs never make one.
    private ConcurrentDictionary<ModelKey, UsageTally>? _otherModels;

    /// <summary>Adds usage that one side-channel call spent on the agent's own model.</summary>
    /// <param name="usage">The call usage details. Ignores <see langword="null"/>.</param>
    public void Add(UsageDetails? usage) => _agentModel.Add(usage);

    /// <summary>Adds usage that one side-channel call spent on another model.</summary>
    /// <param name="usage">The call usage details. Ignores <see langword="null"/>.</param>
    /// <param name="provider">The provider name of the model that spent it.</param>
    /// <param name="model">The name of the model that spent it.</param>
    /// <exception cref="ArgumentException"><paramref name="provider"/> or <paramref name="model"/> is empty.</exception>
    public void Add(UsageDetails? usage, string provider, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        if (usage is null)
        {
            return;
        }

        var otherModels = LazyInitializer.EnsureInitialized(
            ref _otherModels,
            static () => new ConcurrentDictionary<ModelKey, UsageTally>(ModelKeyComparer.Instance));

        otherModels.GetOrAdd(new ModelKey(provider, model), static _ => new UsageTally()).Add(usage);
    }

    /// <summary>Converts the agent's own model share to <see cref="RunUsage"/>.</summary>
    /// <returns><see langword="null"/> when no usage was accumulated for the agent's model.</returns>
    public RunUsage? ToAgentModelUsage() => _agentModel.ToRunUsage();

    /// <summary>Lists the share of every other model that reported usage.</summary>
    /// <returns>
    /// One entry per provider and model, ordered by provider and then by model
    /// (case-insensitive), so a reader that takes "the first" share is
    /// deterministic. Empty when no other model was called.
    /// </returns>
    public IReadOnlyList<SideChannelModelUsage> ToOtherModelUsages()
    {
        if (Volatile.Read(ref _otherModels) is not { } otherModels)
        {
            return [];
        }

        var shares = new List<SideChannelModelUsage>(otherModels.Count);

        foreach (var (key, tally) in otherModels)
        {
            if (tally.ToRunUsage() is { } usage)
            {
                shares.Add(new SideChannelModelUsage(key.Provider, key.Model, usage));
            }
        }

        shares.Sort(static (left, right) =>
        {
            var byProvider = StringComparer.OrdinalIgnoreCase.Compare(left.Provider, right.Provider);

            return byProvider != 0 ? byProvider : StringComparer.OrdinalIgnoreCase.Compare(left.Model, right.Model);
        });

        return shares;
    }

    private readonly record struct ModelKey(string Provider, string Model);

    /// <summary>
    /// Matches provider and model names case-insensitively, the same rule the
    /// catalog lookup of <see cref="RunPricingResolver"/> uses to find a price.
    /// </summary>
    private sealed class ModelKeyComparer : IEqualityComparer<ModelKey>
    {
        public static readonly ModelKeyComparer Instance = new();

        public bool Equals(ModelKey x, ModelKey y)
            => StringComparer.OrdinalIgnoreCase.Equals(x.Provider, y.Provider)
               && StringComparer.OrdinalIgnoreCase.Equals(x.Model, y.Model);

        public int GetHashCode(ModelKey obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Provider),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Model));
    }

    /// <summary>The thread-safe counters of one model's share.</summary>
    private sealed class UsageTally
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
        private int _cachedInputReported;
        private int _reasoningReported;
        private int _audioInputReported;
        private int _audioOutputReported;

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

            if (input == 0 && output == 0 && total == 0 &&
                cachedInput is null && reasoning is null && audioInput is null && audioOutput is null)
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
}

/// <summary>The usage one model other than the agent's spent during a run.</summary>
/// <param name="Provider">The provider name.</param>
/// <param name="Model">The model name.</param>
/// <param name="Usage">The usage that model spent.</param>
internal readonly record struct SideChannelModelUsage(string Provider, string Model, RunUsage Usage);
