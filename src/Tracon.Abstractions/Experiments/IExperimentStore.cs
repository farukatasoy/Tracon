namespace Tracon;

/// <summary>Defines the store for A/B experiments.</summary>
/// <remarks>
/// <strong>DI lifetime — singleton.</strong> Registered as a singleton with
/// <c>TryAdd</c>; a consumer's own registration wins.
/// </remarks>
public interface IExperimentStore
{
    /// <summary>Lists all experiments for a tenant by name.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The experiments.</returns>
    ValueTask<IReadOnlyList<Experiment>> ListAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Gets the experiment with the specified name.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The experiment name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The experiment, or <see langword="null"/> when it does not exist.</returns>
    ValueTask<Experiment?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the <see cref="ExperimentStatus.Running"/> experiment for an agent.
    /// Returns <see langword="null"/> when none exists. An agent can have at most
    /// one running experiment at a time.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="agentName">The agent name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The running experiment, or <see langword="null"/> when none exists.</returns>
    ValueTask<Experiment?> GetRunningAsync(string tenantId, string agentName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates an experiment. Only an <see cref="ExperimentStatus.Draft"/>
    /// experiment can be updated. Updating a started experiment throws an
    /// <see cref="TraconException"/>.
    /// </summary>
    /// <param name="experiment">The experiment to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The saved experiment.</returns>
    ValueTask<Experiment> SaveAsync(Experiment experiment, CancellationToken cancellationToken = default);

    /// <summary>Deletes an experiment. A running experiment must be stopped before deletion.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The experiment name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when deletion occurs.</returns>
    ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the experiment to <see cref="ExperimentStatus.Running"/>. Throws an
    /// <see cref="TraconException"/> when another experiment runs for the same agent.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The experiment name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated experiment.</returns>
    ValueTask<Experiment> StartAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>Moves the experiment to <see cref="ExperimentStatus.Stopped"/>.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The experiment name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated experiment.</returns>
    ValueTask<Experiment> StopAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists experiments across all tenants that are
    /// <see cref="ExperimentStatus.Running"/> and define <see cref="Experiment.Canary"/>.
    /// </summary>
    /// <remarks>
    /// This is a maintenance operation for the canary evaluator. It scans all
    /// tenants for the same reason as <c>IPendingApprovalStore.ExpireAsync</c>.
    /// </remarks>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The running experiments that define a canary policy.</returns>
    ValueTask<IReadOnlyList<Experiment>> ListRunningWithCanaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Defines or removes an experiment canary policy. Pass
    /// <see langword="null"/> for <paramref name="policy"/> to remove it.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="SaveAsync"/>, this method works regardless of experiment
    /// status, Draft or Running. A canary policy can be defined while an experiment
    /// already receives traffic.
    /// </remarks>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The experiment name.</param>
    /// <param name="policy">The new policy, or <see langword="null"/> to remove it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated experiment.</returns>
    ValueTask<Experiment> SetCanaryPolicyAsync(
        string tenantId,
        string name,
        CanaryPolicy? policy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a gradual-increase decision from the canary evaluator. The
    /// experiment remains <see cref="ExperimentStatus.Running"/> and only variant
    /// weights change. Only the canary evaluation service calls this method.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The experiment name.</param>
    /// <param name="variants">The new variant weights. They must total 100.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated experiment.</returns>
    ValueTask<Experiment> AdvanceCanaryRampAsync(
        string tenantId,
        string name,
        IReadOnlyList<ExperimentVariant> variants,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies an automatic rollback decision from the canary evaluator. It moves
    /// the experiment to <see cref="ExperimentStatus.Stopped"/>, restores weights
    /// to the control variant, and writes <see cref="Experiment.RollbackReason"/>.
    /// </summary>
    /// <remarks>
    /// The caller, the canary evaluation service, must write
    /// the audit trail before calling this method. If that write fails, it must
    /// not call this method: a rollback that cannot be audited is not applied.
    /// </remarks>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The experiment name.</param>
    /// <param name="variants">The weights restored to the control variant. They must total 100.</param>
    /// <param name="reason">The rollback reason.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated experiment.</returns>
    ValueTask<Experiment> RollbackCanaryAsync(
        string tenantId,
        string name,
        IReadOnlyList<ExperimentVariant> variants,
        string reason,
        CancellationToken cancellationToken = default);
}
