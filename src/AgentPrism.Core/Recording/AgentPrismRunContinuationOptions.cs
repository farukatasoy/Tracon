namespace AgentPrism;

/// <summary>Interrupted-run continuation settings.</summary>
/// <remarks>
/// Read from the <c>AgentPrism:RunContinuation</c> configuration section. See
/// <c>AgentPrismServiceCollectionExtensions.AddAgentPrism</c>. Continuation is
/// triggered by <see cref="RunReconciliationService"/> right after it claims
/// an orphaned run; it has no effect while
/// <see cref="RunReconciliationOptions.Enabled"/> is <see langword="false"/>,
/// because no run is ever claimed in that case.
/// </remarks>
public sealed class AgentPrismRunContinuationOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "AgentPrism:RunContinuation";

    /// <summary>
    /// Whether an interrupted run is continued automatically. Defaults to
    /// <see langword="false"/>: an orphaned run keeps today's behavior
    /// (closed as <see cref="RunStatus.Failed"/>, nothing further happens)
    /// unless a setup opts in.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The most continuations one interruption chain may have. If a
    /// continuation run is itself interrupted, it counts toward this limit;
    /// once reached, the chain stays <see cref="RunStatus.Failed"/>.
    /// </summary>
    public int MaxAttempts { get; set; } = 1;
}
