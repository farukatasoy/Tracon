namespace AgentPrism;

/// <summary>
/// Represents an A/B experiment that splits traffic between two or more definition versions of the same agent.
/// </summary>
/// <remarks>
/// <para>
/// An experiment can only compare <strong>versions of the same agent</strong>.
/// An experiment across different agents is out of scope because it complicates
/// name resolution.
/// </para>
/// <para>
/// Code-based agents (<see cref="AgentDefinitionOrigin.Code"/>) cannot have an
/// experiment because they have no version history. The endpoint
/// states this clearly with HTTP 400.
/// </para>
/// </remarks>
public sealed record Experiment
{
    /// <summary>Gets the experiment identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the tenant that owns the experiment.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the experiment name. It is unique within the tenant and is used as an API route key.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the name of the agent whose traffic is split.</summary>
    public required string AgentName { get; init; }

    /// <summary>Gets the experiment variants. Their weights must total 100.</summary>
    public required IReadOnlyList<ExperimentVariant> Variants { get; init; }

    /// <summary>Gets the current experiment status.</summary>
    public ExperimentStatus Status { get; init; } = ExperimentStatus.Draft;

    /// <summary>
    /// Gets a reserved value. Runtime assignment does <strong>not read</strong> it
    /// in this phase. The assignment key is always the session identifier, or the
    /// run identifier when no session exists. This property is reserved for future
    /// strategies that assign outside a session.
    /// </summary>
    public string? AssignmentKey { get; init; }

    /// <summary>
    /// Gets the time when the experiment entered <see
    /// cref="ExperimentStatus.Running"/>. Returns <see langword="null"/> while it is a
    /// draft.
    /// </summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>
    /// Gets the time when the experiment stopped. Returns <see langword="null"/> when
    /// it runs or never started.
    /// </summary>
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>Gets the UTC time of the last update.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// Gets the canary policy. <see langword="null"/> disables automatic decisions,
    /// so no background service evaluates this experiment.
    /// </summary>
    public CanaryPolicy? Canary { get; init; }

    /// <summary>
    /// Gets the reason for an automatic rollback. Returns <see langword="null"/>
    /// when the experiment was stopped manually or was never stopped.
    /// </summary>
    public string? RollbackReason { get; init; }
}
