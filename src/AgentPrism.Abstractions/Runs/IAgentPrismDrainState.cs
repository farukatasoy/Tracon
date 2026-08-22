namespace AgentPrism;

/// <summary>Reports whether the process has begun a graceful shutdown drain.</summary>
/// <remarks>
/// Backed by <c>AgentPrismDrainService</c> (AgentPrism.Core), always
/// registered regardless of <c>AgentPrismDrainOptions.Enabled</c> — while
/// disabled, <see cref="IsDraining"/> simply never becomes
/// <see langword="true"/>. Consumers that start a run (the HTTP run
/// endpoints, the job worker) check this before accepting new work.
/// </remarks>
public interface IAgentPrismDrainState
{
    /// <summary>
    /// Gets whether the process has begun stopping and is waiting for
    /// in-flight runs to finish. New runs should not be accepted while this
    /// is <see langword="true"/>.
    /// </summary>
    bool IsDraining { get; }
}
