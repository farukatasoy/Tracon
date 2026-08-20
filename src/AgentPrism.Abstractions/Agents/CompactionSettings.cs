namespace AgentPrism;

/// <summary>
/// Determines how an agent compacts its conversation history.
/// </summary>
/// <remarks>
/// When left <see langword="null"/> no compaction is applied, and the run fails once
/// the conversation history no longer fits the model context window. An invalid
/// combination of fields (for example choosing
/// <see cref="CompactionStrategyKind.Summarization"/> without any trigger) is rejected
/// while the agent is built, with <see cref="AgentPrismCompilationException"/>; it is
/// not ignored silently.
/// </remarks>
public sealed record CompactionSettings
{
    /// <summary>Gets the strategy to apply.</summary>
    public CompactionStrategyKind Strategy { get; init; } = CompactionStrategyKind.None;

    /// <summary>Gets the token count above which compaction is triggered.</summary>
    public int? TriggerTokens { get; init; }

    /// <summary>Gets the message count above which compaction is triggered.</summary>
    public int? TriggerMessages { get; init; }

    /// <summary>Gets the turn count above which compaction is triggered.</summary>
    public int? TriggerTurns { get; init; }

    /// <summary>
    /// Gets the minimum number of turns kept for
    /// <see cref="CompactionStrategyKind.SlidingWindow"/>. 2 is used when it is not given.
    /// </summary>
    public int? MinimumPreservedTurns { get; init; }

    /// <summary>
    /// Gets the minimum number of groups kept for <see cref="CompactionStrategyKind.Truncation"/>,
    /// <see cref="CompactionStrategyKind.ToolResult"/> and
    /// <see cref="CompactionStrategyKind.Summarization"/>. 4 is used when it is not given.
    /// </summary>
    public int? MinimumPreservedGroups { get; init; }

    /// <summary>
    /// Gets the context window size. It is required for
    /// <see cref="CompactionStrategyKind.ContextWindow"/> and ignored by the other strategies.
    /// </summary>
    public int? MaxContextWindowTokens { get; init; }

    /// <summary>
    /// Gets the upper output token limit for <see cref="CompactionStrategyKind.ContextWindow"/>.
    /// When it is not given, <c>ModelBinding.MaxOutputTokens</c> from the agent's own
    /// model binding is used, and 4096 when that is missing too.
    /// </summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>
    /// Gets the extra instruction added to the summarization prompt for
    /// <see cref="CompactionStrategyKind.Summarization"/> and
    /// <see cref="CompactionStrategyKind.Pipeline"/>. When <see langword="null"/> the
    /// default prompt of MAF is used.
    /// </summary>
    public string? SummarizationPrompt { get; init; }

    /// <summary>
    /// Gets the model used for the summarization call. When it is empty the order is
    /// followed: the application-wide helper model setting, and the agent's own model
    /// when that is missing.
    /// </summary>
    public ModelBinding? SummarizationModel { get; init; }
}
