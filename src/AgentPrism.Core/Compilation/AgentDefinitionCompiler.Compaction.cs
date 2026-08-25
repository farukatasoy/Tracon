using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Context compaction strategy production.
/// </summary>
// MAAI001: Microsoft.Agents.AI.Compaction.* is marked "evaluation purposes
// only". Compaction setup is kept in a single file; only this file is
// updated if MAF changes this API. Rationale: docs/KARARLAR.md (same pattern
// as K-020).
#pragma warning disable MAAI001
public sealed partial class AgentDefinitionCompiler
{
    private const int DefaultMinimumPreservedTurns = 2;
    private const int DefaultMinimumPreservedGroups = 4;
    private const int DefaultContextWindowMaxOutputTokens = 4096;

    // The ContextWindow and Pipeline strategies do not expose a
    // CompactionTrigger parameter (they set up/carry their own internal
    // triggers). In these cases, ObservedCompactionStrategy's own trigger is
    // used as a stand-in: the actual gating is left entirely to the internal strategy/strategies.
    private static readonly CompactionTrigger AlwaysTrigger = static _ => true;

    /// <summary>
    /// Builds an executable <see cref="CompactionStrategy"/> from a
    /// definition's compaction settings.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> - the definition does not want compaction
    /// (<see cref="AgentDefinition.Compaction"/> is empty or
    /// <see cref="CompactionStrategyKind.None"/>).
    /// </returns>
    /// <exception cref="AgentPrismCompilationException">
    /// The selected strategy requires a trigger but none was given, or
    /// <see cref="CompactionSettings.MaxContextWindowTokens"/> is missing for
    /// <see cref="CompactionStrategyKind.ContextWindow"/>.
    /// </exception>
    // internal (not private): so structural decisions such as the Pipeline's
    // fixed order can be tested directly. CompactionProvider does not leak
    // this strategy outward, so tests cannot reach it another way.
    internal ObservedCompactionStrategy? BuildCompactionStrategy(AgentDefinition definition)
    {
        var settings = definition.Compaction;

        if (settings is null || settings.Strategy == CompactionStrategyKind.None)
        {
            return null;
        }

        var trigger = BuildTrigger(settings);
        var requiresTrigger = settings.Strategy is not (CompactionStrategyKind.ContextWindow);

        if (requiresTrigger && trigger is null)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' selected the '{settings.Strategy}' compaction strategy " +
                "but gave no trigger (at least one of TriggerTokens/TriggerMessages/TriggerTurns is required).")
            {
                AgentName = definition.Name,
            };
        }

        CompactionStrategy inner = settings.Strategy switch
        {
            CompactionStrategyKind.SlidingWindow => new SlidingWindowCompactionStrategy(
                trigger!, settings.MinimumPreservedTurns ?? DefaultMinimumPreservedTurns, target: null),

            CompactionStrategyKind.Truncation => new TruncationCompactionStrategy(
                trigger!, settings.MinimumPreservedGroups ?? DefaultMinimumPreservedGroups, target: null),

            CompactionStrategyKind.ToolResult => new ToolResultCompactionStrategy(
                trigger!, settings.MinimumPreservedGroups ?? DefaultMinimumPreservedGroups, target: null),

            CompactionStrategyKind.Summarization => new SummarizationCompactionStrategy(
                ResolveSummarizationChatClient(definition),
                trigger!,
                settings.MinimumPreservedGroups ?? DefaultMinimumPreservedGroups,
                settings.SummarizationPrompt,
                target: null),

            CompactionStrategyKind.ContextWindow => BuildContextWindowStrategy(definition, settings),

            // The order is fixed and documented: ToolResult -> SlidingWindow -> Summarization.
            // A free order produces a configuration surface that is hard to
            // understand in the UI.
            CompactionStrategyKind.Pipeline => new PipelineCompactionStrategy(
            [
                new ToolResultCompactionStrategy(
                    trigger!, settings.MinimumPreservedGroups ?? DefaultMinimumPreservedGroups, target: null),
                new SlidingWindowCompactionStrategy(
                    trigger!, settings.MinimumPreservedTurns ?? DefaultMinimumPreservedTurns, target: null),
                new SummarizationCompactionStrategy(
                    ResolveSummarizationChatClient(definition),
                    trigger!,
                    settings.MinimumPreservedGroups ?? DefaultMinimumPreservedGroups,
                    settings.SummarizationPrompt,
                    target: null),
            ]),

            _ => throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' selected an unknown compaction strategy: '{settings.Strategy}'.")
            {
                AgentName = definition.Name,
            },
        };

        return new ObservedCompactionStrategy(inner, trigger ?? AlwaysTrigger, target: null);
    }

    /// <summary>
    /// Builds a single <see cref="CompactionTrigger"/> from the configured
    /// trigger fields. When more than one is set, they are combined so that
    /// any one of them firing triggers compaction.
    /// </summary>
    /// <returns><see langword="null"/> when no trigger field is set.</returns>
    private static CompactionTrigger? BuildTrigger(CompactionSettings settings)
    {
        List<CompactionTrigger>? triggers = null;

        if (settings.TriggerTokens is { } tokens)
        {
            (triggers ??= []).Add(CompactionTriggers.TokensExceed(tokens));
        }

        if (settings.TriggerMessages is { } messages)
        {
            (triggers ??= []).Add(CompactionTriggers.MessagesExceed(messages));
        }

        if (settings.TriggerTurns is { } turns)
        {
            (triggers ??= []).Add(CompactionTriggers.TurnsExceed(turns));
        }

        return triggers switch
        {
            null => null,
            { Count: 1 } single => single[0],
            _ => CompactionTriggers.Any([.. triggers]),
        };
    }

    /// <remarks>
    /// When <see cref="CompactionSettings.MaxContextWindowTokens"/>
    /// is not given, it is DERIVED from <see cref="ModelDescriptor.ContextWindowTokens"/>
    /// in the catalog instead of failing compilation outright — the value the
    /// user would otherwise have to copy in by hand already sits on the model
    /// binding. Compilation fails only when NEITHER source has a value, and
    /// the message names both fields so the user knows which one to fill in.
    /// </remarks>
    private ContextWindowCompactionStrategy BuildContextWindowStrategy(AgentDefinition definition, CompactionSettings settings)
    {
        var maxContextWindowTokens = settings.MaxContextWindowTokens
            ?? FindModelDescriptor(definition.Model.Provider, definition.Model.Model)?.ContextWindowTokens;

        if (maxContextWindowTokens is not { } resolvedMaxContextWindowTokens)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' selected the ContextWindow compaction strategy but did not " +
                $"supply {nameof(CompactionSettings.MaxContextWindowTokens)}, and its model " +
                $"('{definition.Model.Provider}/{definition.Model.Model}') has no context window size in " +
                $"the catalog either. Set {nameof(CompactionSettings.MaxContextWindowTokens)} explicitly.")
            {
                AgentName = definition.Name,
            };
        }

        var maxOutputTokens = settings.MaxOutputTokens
            ?? definition.Model.MaxOutputTokens
            ?? DefaultContextWindowMaxOutputTokens;

        // toolEvictionThreshold/truncationThreshold are not exposed in
        // CompactionSettings in this phase (the doc's §13.1 shape does not
        // include them) - reasonable constant values are used. If needed, add
        // them as a separate field later.
        return new ContextWindowCompactionStrategy(
            resolvedMaxContextWindowTokens,
            maxOutputTokens,
            toolEvictionThreshold: 0.5,
            truncationThreshold: 0.7);
    }

    /// <summary>
    /// Resolves the model used for the summarization call and wraps it with a token tracker.
    /// </summary>
    /// <remarks>
    /// Order: the agent's own <see cref="CompactionSettings.SummarizationModel"/>
    /// → the application-wide utility model → the agent's own model.
    /// </remarks>
    private CompactionUsageTrackingChatClient ResolveSummarizationChatClient(AgentDefinition definition)
    {
        var binding = definition.Compaction?.SummarizationModel ?? _utilityModel ?? definition.Model;

        return new CompactionUsageTrackingChatClient(CreateChatClient(definition, binding));
    }
}
#pragma warning restore MAAI001
