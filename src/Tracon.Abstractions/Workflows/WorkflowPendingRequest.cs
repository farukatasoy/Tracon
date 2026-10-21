namespace Tracon;

/// <summary>
/// Represents human input awaited by a workflow run.
/// </summary>
/// <remarks>
/// <para>
/// The request itself (its prompt and its form) lives in the run's
/// <see cref="RunEventType.WorkflowRequest"/> event, which is append-only and tenant-filtered; it
/// is not copied anywhere. What a listing across runs needs to filter on, and what the expiry
/// needs, is kept apart in <see cref="IWorkflowRequestStore"/>, one row per request; the row names
/// the event it points to, so the two cannot drift apart on the prompt.
/// </para>
/// <para>
/// <strong>A response starts a new run.</strong> Responding to a pending
/// request resumes the run from its checkpoint and creates a new <c>runs</c>
/// row. Reopening the same row would violate the event stream's append-only rule.
/// </para>
/// </remarks>
public sealed record WorkflowPendingRequest
{
    /// <summary>Gets the identifier of the run that produced the request.</summary>
    public required Guid RunId { get; init; }

    /// <summary>
    /// Gets the request identifier. Send this value when responding; the
    /// resumed execution matches it to the request published with the same id.
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>Gets the identifier of the port that published the request. It maps to a graph node.</summary>
    public required string PortId { get; init; }

    /// <summary>Gets the request data type name.</summary>
    public string? RequestType { get; init; }

    /// <summary>Gets the expected response type name.</summary>
    public string? ResponseType { get; init; }

    /// <summary>
    /// Gets the request text shown to the user. For plan approval, this is the plan itself.
    /// </summary>
    public string? Prompt { get; init; }

    /// <summary>
    /// Gets the input field the UI should display.
    /// </summary>
    public required WorkflowRequestForm Form { get; init; }

    /// <summary>Gets the UTC time when the request was published.</summary>
    public DateTimeOffset RequestedAt { get; init; }

    /// <summary>Gets the name of the workflow whose run waits on the request.</summary>
    public string? WorkflowName { get; init; }

    /// <summary>
    /// Gets the execution session of the run. The runs that continue one another share it, and
    /// the same request id in the same session is one request.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets when the request expires (UTC). <see langword="null"/> when the workflow sets no
    /// request timeout, or when the request was published before requests had an expiry.
    /// </summary>
    /// <remarks>
    /// Fixed when the run starts waiting: editing the workflow later does not move it. When the
    /// time passes, the run is closed and an answer is refused like an answer to any run that is
    /// no longer waiting.
    /// </remarks>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Gets a value indicating whether somebody answered the request. An answered run stays
    /// <c>AwaitingInput</c> and keeps listing its request; the answer continues on a new run, which
    /// <c>lastResponseRunId</c> names.
    /// </summary>
    public bool Answered { get; init; }

    /// <summary>
    /// Gets the run that carried out the latest answer; <see langword="null"/> while the request
    /// is unanswered. An answer that stopped before it finished is replaced by the answer that
    /// started again, so this names the run to watch.
    /// </summary>
    public Guid? LastResponseRunId { get; init; }
}

/// <summary>
/// Defines how the UI presents a pending request.
/// </summary>
/// <remarks>
/// <para>
/// The server derives the form from the port's <em>response type</em>. It does
/// this deliberately so clients do not need to resolve .NET type names in the
/// wire contract.
/// </para>
/// <para>Serialized as a JSON string name.</para>
/// </remarks>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<WorkflowRequestForm>))]
public enum WorkflowRequestForm
{
    /// <summary>Free-form JSON. The UI displays a text box and sends its content unchanged.</summary>
    Json = 0,

    /// <summary>Plain-text response.</summary>
    Text = 1,

    /// <summary>Yes or no response.</summary>
    Boolean = 2,

    /// <summary>
    /// Plan approval: approve the plan or return it with revision text.
    /// This is the plan-review flow of the <c>Magentic</c> pattern.
    /// </summary>
    PlanReview = 3,

    /// <summary>
    /// A tool call that waits for approval. Answer with <see cref="WorkflowRespondRequest.Approved"/>, an
    /// optional reason in <see cref="WorkflowRespondRequest.Text"/>, and optionally
    /// <see cref="WorkflowRespondRequest.Remember"/>. The tool call itself is never sent back: the
    /// answer is built from the request the workflow recorded.
    /// </summary>
    ToolApproval = 4,
}

/// <summary>Represents a response to a pending request.</summary>
/// <remarks>
/// The fields are not mutually exclusive. The port response type determines
/// which one is used. The request is <strong>rejected</strong> when no field
/// converts to the expected type; silently accepting a response of the wrong
/// type would fail execution at an opaque point.
/// </remarks>
public sealed record WorkflowRespondRequest
{
    /// <summary>Gets the identifier of the run being responded to.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Gets the identifier of the request being responded to.</summary>
    public required string RequestId { get; init; }

    /// <summary>Gets the yes/no response; for plan approval, it means "approve the plan".</summary>
    public bool? Approved { get; init; }

    /// <summary>Gets the text response; for plan approval, it is revision guidance.</summary>
    public string? Text { get; init; }

    /// <summary>Gets the free-form JSON response. It is deserialized to the port response type.</summary>
    public string? Json { get; init; }

    /// <summary>
    /// Gets whether an approval is remembered as a rule ("do not ask again") for the agent and the tool.
    /// </summary>
    /// <remarks>
    /// Only for a <see cref="WorkflowRequestForm.ToolApproval"/> request, and only together with
    /// <see cref="Approved"/> set to <see langword="true"/>; any other use is refused.
    /// </remarks>
    public bool? Remember { get; init; }

    /// <summary>
    /// Gets the identifier of the checkpoint to resume. If null, the run's most
    /// recent checkpoint is used.
    /// </summary>
    public string? CheckpointId { get; init; }

    /// <summary>Gets the identifier of the new run. When supplied, recording starts with this id.</summary>
    public Guid? NewRunId { get; init; }

    /// <summary>
    /// Gets the version of the workflow's stored definition to continue with. See
    /// <see cref="WorkflowResumeRequest.DefinitionVersion"/>: <see langword="null"/> continues with
    /// the version the waiting run recorded.
    /// </summary>
    public int? DefinitionVersion { get; init; }
}
