namespace AgentPrism;

/// <summary>Summary of a run. It acts as the header of the event stream.</summary>
public sealed record RunRecord
{
    /// <summary>Gets the run id. A time-ordered UUID (v7).</summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets the name of the agent that ran. For workflow runs this is the workflow
    /// name: the existing lists, statistics and user interface read this column, and
    /// leaving it empty would show workflow rows without a name.
    /// </summary>
    public required string AgentName { get; init; }

    /// <summary>Gets whether this row records an agent or a workflow.</summary>
    public RunKind Kind { get; init; }

    /// <summary>
    /// Gets the name of the workflow. Populated only on
    /// <see cref="RunKind.Workflow"/> rows.
    /// </summary>
    public string? WorkflowName { get; init; }

    /// <summary>Gets the current status of the run.</summary>
    public required RunStatus Status { get; init; }

    /// <summary>Gets the start time (UTC).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Gets the completion time (UTC), or <see langword="null"/> while the run is in flight.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Gets the tenant the run belongs to.</summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Gets the user the run belongs to, or <see langword="null"/> when it was
    /// not known.
    /// </summary>
    /// <remarks>
    /// The value is opaque and comes from
    /// <see cref="IRunAttributionContext"/>, never from the request body. Rows
    /// written before the column existed hold <see langword="null"/>; they are
    /// not backfilled.
    /// </remarks>
    public string? UserId { get; init; }

    /// <summary>
    /// Gets the labels the run carries, or <see langword="null"/> when it
    /// carries none.
    /// </summary>
    /// <remarks>
    /// A query dimension only. Labels never become metric tags — see
    /// <see cref="IRunAttributionContext.Labels"/>.
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Labels { get; init; }

    /// <summary>Gets the id of the session that was used.</summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets the model used by the run. Cost and per-model reports need it. It is
    /// <see langword="null"/> when the agent definition carries no model.
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>Gets whether the run streamed.</summary>
    public bool IsStreaming { get; init; }

    /// <summary>Gets the token usage, or <see langword="null"/> when the provider reported none.</summary>
    public RunUsage? Usage { get; init; }

    /// <summary>Gets the error information. Populated only for <see cref="RunStatus.Failed"/>.</summary>
    public RunError? Error { get; init; }

    /// <summary>Gets the number of events written for this run.</summary>
    public long EventCount { get; init; }

    /// <summary>
    /// Gets the id of the run that started this one, or <see langword="null"/> for a
    /// root run.
    /// </summary>
    public Guid? ParentRunId { get; init; }

    /// <summary>
    /// Gets the id of the run at the root of the tree. <see langword="null"/> for a
    /// root run and always populated for a child run.
    /// </summary>
    /// <remarks>
    /// Denormalized on purpose: a whole tree is fetched with a single indexed query
    /// on this column, with no recursive CTE over <see cref="ParentRunId"/>.
    /// </remarks>
    public Guid? RootRunId { get; init; }

    /// <summary>Gets the depth in the tree. The root run is 0.</summary>
    public int Depth { get; init; }

    /// <summary>Gets the definition version this run measured, or <see langword="null"/> when unknown.</summary>
    public int? AgentVersion { get; init; }

    /// <summary>Gets the experiment this run belongs to, or <see langword="null"/> outside an experiment.</summary>
    public Guid? ExperimentId { get; init; }

    /// <summary>Gets the experiment variant this run was assigned to, or <see langword="null"/> outside an experiment.</summary>
    public string? Variant { get; init; }

    /// <summary>Gets the number of <em>direct</em> child runs.</summary>
    public int ChildRunCount { get; init; }

    /// <summary>
    /// Gets the total token usage of this run and every run below it.
    /// </summary>
    /// <remarks>
    /// This <strong>must not be added</strong> to <see cref="Usage"/>: the value
    /// already includes <see cref="Usage"/>. For a run without children the two are
    /// equal. The user interface shows them in separate columns, because "what did
    /// this run spend" and "what did this request spend in total" are different
    /// questions.
    /// </remarks>
    public RunUsage? TreeUsage { get; init; }

    /// <summary>
    /// Gets the run's own cost. <see langword="null"/> when the model is unknown;
    /// when the model is known the value is populated even if the price is not.
    /// </summary>
    public RunCost? Cost { get; init; }

    /// <summary>
    /// Gets the total cost of this run and every run below it.
    /// </summary>
    /// <remarks>
    /// This <strong>must not be added</strong> to <see cref="Cost"/>: the value
    /// already includes the run's own cost, for the same reason as
    /// <see cref="TreeUsage"/>. For a run without children the two are equivalent.
    /// </remarks>
    public RunTreeCost? TreeCost { get; init; }

    /// <summary>
    /// Gets the source run id when this run is a replay, otherwise
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The lineage is one-way: the source run is <strong>immutable</strong> and does
    /// not know which runs replay it. Every replay of a source is found through this
    /// column.
    /// </remarks>
    public Guid? ReplayOfRunId { get; init; }
}
