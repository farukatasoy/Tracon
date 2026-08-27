namespace AgentPrism;

/// <summary>
/// Reads the workflow catalog and runs workflows.
/// </summary>
/// <remarks>
/// <para>
/// The abstraction lives in <c>AgentPrism.Abstractions</c> because the HTTP
/// layer runs workflows but does <strong>not</strong> depend on the
/// <c>AgentPrism.Workflows</c> package. This is the same pattern established
/// with <see cref="IMcpToolRefresher"/> in MCP: the workflow engine stays an
/// optional package, and a consumer who does not use it does not pull in a
/// 130-type execution engine.
/// </para>
/// <para>
/// The contract carries no Microsoft Agent Framework type. Events flow
/// through AgentPrism's own <see cref="RunEvent"/> type; the conversion from
/// MAF events happens inside <c>AgentPrism.Workflows</c>.
/// </para>
/// <para>
/// <strong>Delivery guarantee — AT-LEAST-ONCE, on <see cref="ResumeStreamingAsync"/>
/// specifically.</strong> Resuming from a run's LATEST checkpoint after it has
/// already completed does not re-run anything — there is nothing left to do. But
/// resuming from an EARLIER checkpoint (the shape a real crash recovery takes)
/// replays every super-step from that point forward, including any
/// <c>AddWorkflowFunction&lt;T&gt;()</c> handler node in them, with the SAME
/// input — this is exactly the reason a workflow function handler with a real
/// side effect must be idempotent (see <c>AddWorkflowFunction&lt;T&gt;()</c>'s
/// own remarks, where this was originally documented). <see cref="RunStreamingAsync"/>
/// and <see cref="RespondStreamingAsync"/> carry no such guarantee of their own:
/// each opens a fresh run and does not replay prior work.
/// </para>
/// </remarks>
public interface IWorkflowRunner
{
    /// <summary>Lists the workflows in the catalog (defined in code + database).</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The summaries.</returns>
    ValueTask<IReadOnlyList<WorkflowDescriptor>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetches a single workflow's summary.</summary>
    /// <param name="name">The workflow name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The summary; <see langword="null"/> if it does not exist.</returns>
    ValueTask<WorkflowDescriptor?> GetAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compiles a workflow and extracts its graph.
    /// </summary>
    /// <param name="name">The workflow name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The graph; <see langword="null"/> if the workflow is not in the catalog.</returns>
    /// <remarks>
    /// The graph is <strong>actually compiled</strong>: the helper nodes added
    /// by prebuilt patterns are visible only after compilation, and the
    /// executor identifiers in run events come from there too. Compilation
    /// makes no agent call, it only wires up the wrappers.
    /// </remarks>
    ValueTask<WorkflowGraph?> GetGraphAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a run's pending human-input requests.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The pending requests; an empty list if none.</returns>
    /// <exception cref="AgentPrismException">
    /// The run does not exist or belongs to another tenant.
    /// </exception>
    ValueTask<IReadOnlyList<WorkflowPendingRequest>> ListPendingRequestsAsync(
        Guid runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Responds to a pending request and resumes the run from its checkpoint.
    /// </summary>
    /// <param name="request">The response.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ordered event stream.</returns>
    /// <remarks>
    /// Resuming opens a <strong>new run record</strong>; the same rule as
    /// <see cref="ResumeStreamingAsync"/>. The response is matched to the
    /// request republished from the checkpoint by identifier.
    /// </remarks>
    IAsyncEnumerable<RunEvent> RespondStreamingAsync(
        WorkflowRespondRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a workflow and streams its events.
    /// </summary>
    /// <param name="request">The run request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ordered event stream.</returns>
    /// <remarks>
    /// The first event is always <see cref="RunEventType.RunStarted"/>, and
    /// its <see cref="RunEvent.RunId"/> field carries the run identifier; the
    /// caller learns the identifier from the stream's first frame.
    /// </remarks>
    IAsyncEnumerable<RunEvent> RunStreamingAsync(
        WorkflowRunRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes from a checkpoint and streams its events.
    /// </summary>
    /// <param name="request">The resume request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ordered event stream.</returns>
    /// <remarks>
    /// Resuming opens a <strong>new run record</strong>. Reopening the same
    /// row would break the event stream's append-only rule and would
    /// leave "when did this run end" unanswered.
    /// </remarks>
    IAsyncEnumerable<RunEvent> ResumeStreamingAsync(
        WorkflowResumeRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>The information needed to run a workflow.</summary>
public sealed record WorkflowRunRequest
{
    /// <summary>The name of the workflow to run.</summary>
    public required string WorkflowName { get; init; }

    /// <summary>The user message to feed into the graph.</summary>
    public string? Message { get; init; }

    /// <summary>
    /// The execution session's identifier. Generated if left empty.
    /// </summary>
    /// <remarks>
    /// The value <strong>comes from the client and is untrusted input</strong>.
    /// Since checkpoints are grouped under this value, using it without
    /// validation means access to another execution's state.
    /// </remarks>
    public string? SessionId { get; init; }

    /// <summary>
    /// The run identifier. If given, the record is opened with this
    /// identifier; a streaming endpoint uses this to know the identifier
    /// before writing the first frame.
    /// </summary>
    public Guid? RunId { get; init; }
}

/// <summary>The information needed to resume a workflow from a checkpoint.</summary>
public sealed record WorkflowResumeRequest
{
    /// <summary>The identifier of the run to resume.</summary>
    public required Guid RunId { get; init; }

    /// <summary>
    /// The identifier of the checkpoint to resume from. If left empty, that
    /// run's <em>most recent</em> checkpoint is used.
    /// </summary>
    public string? CheckpointId { get; init; }

    /// <summary>The new run's identifier. If given, the record is opened with this identifier.</summary>
    public Guid? NewRunId { get; init; }
}
