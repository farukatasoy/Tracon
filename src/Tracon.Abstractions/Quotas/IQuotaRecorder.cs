namespace Tracon;

/// <summary>
/// The quota counters as a call tree sees them: where a run writes its consumption, and the
/// check at the start of new work inside a tree that is already running. The seam
/// <see cref="TreeAccount"/> carries so that a run which ends after its root, and a tool call
/// that ends after its run, can still write.
/// </summary>
/// <remarks>
/// Internal on purpose: the only implementation is <c>QuotaEnforcer</c>, and a
/// consumer replaces the counters' storage (<see cref="IQuotaStore"/>), not the
/// rule that decides what a run adds to them or what it may start.
/// </remarks>
internal interface IQuotaRecorder
{
    /// <summary>
    /// Gets whether quota enforcement is on. When it is off nothing is written and nothing is
    /// refused, so a tree has no use for the cost of a model call it would only report to a quota.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>Adds the consumption to the counters and claims the thresholds it newly crosses.</summary>
    /// <param name="consumption">The consumption to add.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The thresholds this consumption newly crossed; empty when quotas are off or the write failed.</returns>
    ValueTask<IReadOnlyList<QuotaThresholdCrossing>> RecordAsync(
        QuotaConsumption consumption,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decides whether a run tree that is already running may start new work against its tenant's quota.
    /// </summary>
    /// <param name="account">The tree's account: whose counter the tree adds to.</param>
    /// <param name="kind">What is about to start. It decides which limits count.</param>
    /// <param name="unrecordedTokens">The tokens the tree has spent that its counter has not seen.</param>
    /// <param name="unrecordedCost">The amount the tree has spent that its counter has not seen.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The decision. A store that cannot be read is answered as <c>AllowOnStoreFailure</c> says.</returns>
    ValueTask<QuotaDecision> CheckTreeStartAsync(
        TreeAccount account,
        TreeStartKind kind,
        long unrecordedTokens,
        decimal unrecordedCost,
        CancellationToken cancellationToken = default);
}

/// <summary>What a run tree that is already running is about to start.</summary>
internal enum TreeStartKind
{
    /// <summary>
    /// A workflow function node. It spends no tokens and opens no child run, so only the amount it
    /// may spend counts: the tree's budget does not stop a node on tokens either.
    /// </summary>
    FunctionNode,

    /// <summary>
    /// An agent step of a workflow, or a call from one agent to another. Tokens and the amount count.
    /// A child run adds no run to the counter (<c>Runs = 0</c>), so the run limit does not.
    /// </summary>
    AgentStep,
}
