namespace AgentPrism;

/// <summary>
/// Carries the identity, tree position, and budget of the running run to the
/// helper components inside the run path.
/// </summary>
/// <remarks>
/// <para>
/// It has two consumers. Skill script execution is triggered from inside MAF,
/// <em>below</em> the recording wrapper; sub-agent invocation is triggered
/// from MAF's background task tool. Neither has a way to pass the run
/// identity as a parameter: the call chain belongs to MAF.
/// </para>
/// <para>
/// 🚨 The value is held in an <see cref="AsyncLocal{T}"/>. This means the
/// assignment <strong>does not flow back to the caller</strong>: the
/// <c>Set</c> call must happen in the <em>own body</em> of the method that
/// starts the run. The same trap happened with
/// <see cref="System.Diagnostics.Activity.Current"/> in Phase 6.
/// </para>
/// <para>
/// The value <strong>flows downward</strong>: because Microsoft Agent
/// Framework's background agent task captures <c>ExecutionContext</c>, a
/// sub-agent running on a different thread also sees the same scope.
/// Measured in Phase 12.
/// </para>
/// </remarks>
public static class AgentPrismRunContext
{
    private static readonly AsyncLocal<AgentRunScope?> ScopeHolder = new();

    /// <summary>Gets the scope of the running run. <see langword="null"/> outside a run.</summary>
    public static AgentRunScope? Current => ScopeHolder.Value;

    /// <summary>Gets the identity of the running run. <see langword="null"/> outside a run.</summary>
    public static Guid? CurrentRunId => ScopeHolder.Value?.RunId;

    /// <summary>Sets the scope of the running run.</summary>
    /// <param name="scope">The scope. If <see langword="null"/>, the scope is cleared.</param>
    public static void SetCurrent(AgentRunScope? scope) => ScopeHolder.Value = scope;
}

/// <summary>
/// Represents the view of a running run that is exposed to the helper
/// components in the run path.
/// </summary>
/// <remarks>
/// The scope is opened by <see cref="RunRecordingAgent"/>. A sub-agent
/// invocation builds its own <see cref="AgentPrismRunOptions"/> object from
/// the values it reads here; this is how tree linkage, depth, and budget are
/// carried through the call chain.
/// </remarks>
public sealed record AgentRunScope
{
    /// <summary>Gets the identity of the running run.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Gets the identity of the run at the root of the tree. Equal to <see cref="RunId"/> at the root.</summary>
    public required Guid RootRunId { get; init; }

    /// <summary>Gets the depth in the tree. The root run is 0.</summary>
    public int Depth { get; init; }

    /// <summary>Gets the name of the agent running this run.</summary>
    public string? AgentName { get; init; }

    /// <summary>Gets the tenant of the run. A sub-run cannot leave this tenant.</summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Gets the session the run belongs to. <see langword="null"/> for a sessionless run.
    /// </summary>
    /// <remarks>
    /// 🚨 Content that a tool produces and writes to the <c>attachments</c>
    /// table MUST CARRY this field. The retention policy (Phase 25) treats an
    /// attachment with an empty <c>session_id</c> field as <strong>orphaned</strong>
    /// and deletes it after the cutoff date; the content in the transcript is
    /// then lost while the session is still alive. A tool cannot access
    /// <c>AgentSession</c>, so this is the only place it can read the session
    /// identity from. Rationale: <c>docs/28-SES-TOOLLARI.md</c>, section 28.0/G1.
    /// </remarks>
    public string? SessionId { get; init; }

    /// <summary>Gets the budget shared across the tree.</summary>
    public AgentRunBudget? Budget { get; init; }

    /// <summary>Gets the definition version this run measures. <see langword="null"/> if unknown.</summary>
    public int? AgentVersion { get; init; }

    /// <summary>Gets the identity of the experiment this run belongs to. <see langword="null"/> for a run outside an experiment.</summary>
    public Guid? ExperimentId { get; init; }

    /// <summary>Gets the name of the experiment arm this run is assigned to. <see langword="null"/> for a run outside an experiment.</summary>
    public string? Variant { get; init; }

    /// <summary>
    /// Gets the event writer for this run. A sub-run writes its summary events here.
    /// </summary>
    /// <remarks>
    /// The sequence number is produced by <strong>a single writer</strong>
    /// (decision K-014). If a sub-call set up its own writer, the same run
    /// would end up with two independent counters and the sequence numbers
    /// would collide.
    /// </remarks>
    public RunEventWriter? Writer { get; init; }

    /// <summary>
    /// Gets the accumulator that collects the extra token usage produced by
    /// context compaction (summarization). It is folded into the final usage
    /// at the end of the run.
    /// </summary>
    internal CompactionUsageAccumulator? ExtraUsage { get; init; }

    /// <summary>
    /// Gets the non-token metrics reported by tools, keyed by call identity.
    /// </summary>
    /// <remarks>
    /// The write surface is <see cref="AgentPrismToolUsage.Report"/>; the read
    /// side is in <c>ToolInvocationTracker</c>.
    /// </remarks>
    internal ToolUsageAccumulator? ToolUsage { get; init; }
}
