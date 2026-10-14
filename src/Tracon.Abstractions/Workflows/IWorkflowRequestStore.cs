using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>
/// The inbox of a tenant's open workflow requests: one row per request a run is waiting on,
/// with the time the request expires and whether somebody answered it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is a projection, not the request.</strong> The request itself (its prompt,
/// its form) stays in the run's <see cref="RunEventType.WorkflowRequest"/> event; a row names
/// that event by its sequence number and carries only what a listing and the expiry need to
/// filter on. A row is written once, when the run closes as <see cref="RunStatus.AwaitingInput"/>,
/// and a row that cannot be written does not stop the run: the request is still listed by
/// <c>GET /api/workflows/runs/{runId}/requests</c> and can still be answered, but it is missing
/// from the tenant-wide list and never expires.
/// </para>
/// <para>
/// <strong>Tenant behavior.</strong> <see cref="ListAsync"/>, <see cref="ListByRunAsync"/> and
/// <see cref="MarkAnsweredAsync"/> see only the caller's tenant (<c>ITenantContext</c>); a write
/// uses the tenant the record carries. <see cref="ExpireAsync"/> is maintenance work and scans
/// every tenant, the same rationale as <c>IPendingApprovalStore.ExpireAsync</c>.
/// </para>
/// <para>
/// <strong>DI lifetime.</strong> Registered as a singleton with <c>TryAdd</c>; a consumer's own
/// registration wins, and each SQL provider replaces the in-memory default. An implementation
/// must be safe under concurrent calls: an answer and the expiry of the same request arrive
/// together by design.
/// </para>
/// <para>
/// <strong>Delivery.</strong> No delivery guarantee applies: every call completes or throws before it
/// returns, and nothing is sent later. The engine's write of a run's rows and the endpoint's
/// <see cref="MarkAnsweredAsync"/> call are best-effort: a failure is logged and the run, or the
/// answer, goes on. <see cref="ExpireAsync"/> hands each closed row to exactly one caller.
/// </para>
/// <para>
/// A SQL store ties a row to its run with a cascading foreign key, so a data subject erasure that
/// removes the run removes its rows. A row of a run that was closed is deleted by the
/// <see cref="RetentionTargets.WorkflowRequests"/> retention target.
/// </para>
/// </remarks>
public interface IWorkflowRequestStore
{
    /// <summary>Writes the rows of the requests a run is waiting on.</summary>
    /// <param name="records">
    /// The rows. Every <see cref="WorkflowRequestRecord.TenantId"/> must be set and every
    /// <see cref="WorkflowRequestRecord.Status"/> must be <see cref="WorkflowRequestStatus.Open"/>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// A row whose run and request id are already stored is left as it is, so writing the same
    /// run twice is harmless. The run must exist: a SQL store refuses a row for a run it does not
    /// hold.
    /// </remarks>
    ValueTask AddAsync(IReadOnlyList<WorkflowRequestRecord> records, CancellationToken cancellationToken = default);

    /// <summary>Lists the caller's open requests, one per logical request, oldest first.</summary>
    /// <param name="query">The filter and the page.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The page of rows.</returns>
    /// <remarks>
    /// <para>
    /// A request is logical: the same request id in the same execution session is one request even
    /// when several runs of the session wait on it (a run that is answered stays
    /// <see cref="RunStatus.AwaitingInput"/>, and the continuation republishes the requests that
    /// are still unanswered). The listing returns the newest open row of each, so the row names the
    /// run to answer.
    /// </para>
    /// <para>
    /// The state filter and the page are applied after that choice, in the store, so a page is never
    /// shorter than <see cref="WorkflowRequestQuery.Take"/> unless the listing ends.
    /// </para>
    /// <para>
    /// The order is <see cref="WorkflowRequestRecord.RequestedAt"/>, then run id, then request id; it
    /// is stable, so <see cref="WorkflowRequestQuery.Skip"/> pages without gaps or repeats while
    /// nothing changes.
    /// </para>
    /// </remarks>
    ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListAsync(
        WorkflowRequestQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every row of one run, open or closed, in request order.</summary>
    /// <param name="runId">The run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rows of the caller's tenant; empty when the run has none.</returns>
    ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListByRunAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a request was answered: every open row of the logical request (the same
    /// request id in the same session, whichever run holds it) becomes answered.
    /// </summary>
    /// <param name="sessionId">The execution session the request belongs to.</param>
    /// <param name="requestId">The request id.</param>
    /// <param name="responseRunId">The run the answer started.</param>
    /// <param name="answeredAt">When the answer was accepted (UTC).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many rows changed.</returns>
    /// <remarks>
    /// Marking a request that is already answered replaces the run it names and keeps the first
    /// <see cref="WorkflowRequestRecord.AnsweredAt"/>: the answer that replaces a stopped one
    /// (<see cref="WorkflowResponseStatus.Abandoned"/>) names its own run. A row that is closed is
    /// not touched.
    /// </remarks>
    ValueTask<int> MarkAnsweredAsync(
        string sessionId,
        string requestId,
        Guid responseRunId,
        DateTimeOffset answeredAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the open rows whose <see cref="WorkflowRequestRecord.ExpiresAt"/> has passed and
    /// returns them.
    /// </summary>
    /// <param name="now">Rows that expire at or before this time are closed (UTC).</param>
    /// <param name="max">The upper number of rows closed in this round.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The rows that were closed (<see cref="WorkflowRequestRecord.Status"/> is
    /// <see cref="WorkflowRequestStatus.Closed"/>); <see cref="WorkflowRequestRecord.AnsweredAt"/> tells
    /// whether the request was answered. The order of the rows is not defined.
    /// </returns>
    /// <remarks>
    /// <c>[TenantAgnostic]</c>: this is maintenance work and scans the expired rows of every tenant;
    /// filtering by the ambient tenant would leave the requests of the other tenants open forever.
    /// Two callers that close the same row are decided by the store: one receives the row.
    /// </remarks>
    ValueTask<IReadOnlyList<WorkflowRequestRecord>> ExpireAsync(
        DateTimeOffset now,
        int max,
        CancellationToken cancellationToken = default);
}

/// <summary>Whether a run still waits on a request.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkflowRequestStatus>))]
public enum WorkflowRequestStatus
{
    /// <summary>The run waits on the request.</summary>
    Open = 0,

    /// <summary>The request expired and the run was closed for it.</summary>
    Closed = 1,
}

/// <summary>One request a workflow run waits on, as the inbox stores it.</summary>
public sealed record WorkflowRequestRecord
{
    /// <summary>Gets the run that waits on the request.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Gets the request id the answer must carry.</summary>
    public required string RequestId { get; init; }

    /// <summary>Gets the execution session the run belongs to. Runs that continue one another share it.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the name of the workflow.</summary>
    public required string WorkflowName { get; init; }

    /// <summary>Gets the id of the port that published the request; empty when the event did not carry it.</summary>
    public string PortId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the sequence number of the run's <see cref="RunEventType.WorkflowRequest"/> event that
    /// holds the request, where the prompt and the form are read.
    /// </summary>
    public required long EventSequence { get; init; }

    /// <summary>Gets when the run published the request (UTC).</summary>
    public required DateTimeOffset RequestedAt { get; init; }

    /// <summary>
    /// Gets when the request expires (UTC), or <see langword="null"/> when the workflow sets no
    /// <see cref="WorkflowDefinition.RequestTimeout"/>. Fixed when the row is written: a later
    /// edit of the workflow does not move it.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Gets whether the run still waits on the request.</summary>
    public WorkflowRequestStatus Status { get; init; }

    /// <summary>Gets when the request was first answered (UTC); <see langword="null"/> while nobody has answered.</summary>
    public DateTimeOffset? AnsweredAt { get; init; }

    /// <summary>Gets the run that carried out the latest answer; <see langword="null"/> while nobody has answered.</summary>
    public Guid? LastResponseRunId { get; init; }

    /// <summary>Gets when the row was closed (UTC); <see langword="null"/> while it is open.</summary>
    public DateTimeOffset? ClosedAt { get; init; }

    /// <summary>Gets the tenant the row belongs to. Set when it is written; not part of any HTTP response.</summary>
    [JsonIgnore]
    public string? TenantId { get; init; }
}
