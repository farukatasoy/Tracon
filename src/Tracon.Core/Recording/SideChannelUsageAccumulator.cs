using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Accumulates token usage produced by a model call that runs OUTSIDE the
/// agent's normal request/response flow during a run.
/// </summary>
/// <remarks>
/// <para>
/// Three sources feed it today. Two of them call ANOTHER model through their own
/// <see cref="IChatClient"/>, fully separate from the agent's
/// <c>AgentResponse</c>: the context compaction summarizer and the
/// <c>aiJudge</c> loop criterion's judge (both through
/// <see cref="SideChannelUsageTrackingChatClient"/>; the detector of a content-filtered or truncated
/// side-channel response adds the response it rejects under the model that answered it). One spends on the
/// agent's OWN model but throws the response away: a bounded structured-response
/// repair turn that a repair supersedes (<see cref="StructuredResponseValidatingAgent"/>). None of these
/// tokens enter the normal flow on their own, so this accumulator is available
/// through <see cref="TraconRunContext"/>.
/// </para>
/// <para>
/// The agent's own model is also counted as it answers, by a separate accumulator
/// (<see cref="ObservedUsageAccumulator"/>), for the run that ends in an exception or a cancellation and so has
/// no answer to read its usage from. That run reads the observed count for the agent's own model and does not
/// add the agent's own share kept here, which it already holds.
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
}

/// <summary>The usage one model other than the agent's spent during a run.</summary>
/// <param name="Provider">The provider name.</param>
/// <param name="Model">The model name.</param>
/// <param name="Usage">The usage that model spent.</param>
internal readonly record struct SideChannelModelUsage(string Provider, string Model, RunUsage Usage);
