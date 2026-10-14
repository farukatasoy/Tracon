using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>
/// Where the answers to a workflow's pending human requests are recorded: which run
/// answered which request of which run, who answered, and whether the single answer a
/// <see cref="WorkflowResponseMode.SingleAnswer"/> workflow allows is already taken.
/// </summary>
/// <remarks>
/// <para>
/// The store keeps the decision, not its content: a record carries a SHA-256 of the
/// answer, never the answer itself. The request is still read from the waiting run's
/// <see cref="RunEventType.WorkflowRequest"/> event.
/// </para>
/// <para>
/// <strong>Tenant behavior.</strong> Reads and updates use the ambient tenant, read from
/// <c>ITenantContext</c>: <see cref="FindClaimAsync"/>, <see cref="ReplaceAsync"/> (the record
/// it abandons), <see cref="MarkDeliveredAsync"/>, <see cref="ListBySourceRunAsync"/> and
/// <see cref="GetAsync"/> see only the caller's tenant. A write uses the expected tenant the
/// record carries (<see cref="WorkflowResponseRecord.TenantId"/>). Two tenants can hold a claim
/// with the same scope and request id without seeing each other.
/// </para>
/// <para>
/// <strong>DI lifetime.</strong> Registered as a singleton with <c>TryAdd</c>; a consumer's own
/// registration wins, and each SQL provider replaces the in-memory default. An implementation
/// must be safe under concurrent calls: two answers to one request arrive together by design.
/// </para>
/// <para>
/// <strong>Delivery.</strong> No delivery guarantee applies: every call completes or throws
/// before it returns, and nothing is sent later. The respond endpoint refuses an answer whose
/// record cannot be written; the engine's <see cref="MarkDeliveredAsync"/> call is best-effort
/// and only logged when it fails.
/// </para>
/// <para>
/// A record is deleted only with its source run: a SQL store ties it to the run with a
/// cascading foreign key, and a data subject erasure that removes the source run removes its
/// records too. No retention target deletes runs, so the records of an installation grow
/// until such an erasure; plan for that the way you plan for the run history itself.
/// </para>
/// </remarks>
public interface IWorkflowResponseStore
{
    /// <summary>
    /// Records an answer. When the record carries a <see cref="WorkflowResponseRecord.ClaimScope"/>,
    /// it is written only if no other active record holds the same scope and request id.
    /// </summary>
    /// <param name="record">
    /// The answer to record. <see cref="WorkflowResponseRecord.Status"/> must be
    /// <see cref="WorkflowResponseStatus.Accepted"/> and <see cref="WorkflowResponseRecord.TenantId"/>
    /// must be set.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see cref="WorkflowResponseClaim.Claimed"/> is <see langword="true"/> when the record was
    /// written. It is <see langword="false"/> when another active record already holds the claim;
    /// <see cref="WorkflowResponseClaim.Existing"/> is that record.
    /// </returns>
    /// <remarks>
    /// Two concurrent writes with the same claim are decided by the store, atomically: exactly
    /// one of them is written. A record without a claim scope is always written.
    /// </remarks>
    ValueTask<WorkflowResponseClaim> AddAsync(WorkflowResponseRecord record, CancellationToken cancellationToken = default);

    /// <summary>Finds the active record that holds a claim.</summary>
    /// <param name="claimScope">The claim scope (the execution session of the waiting run).</param>
    /// <param name="requestId">The request id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The record, or <see langword="null"/> when no record of the caller's tenant that is not
    /// <see cref="WorkflowResponseStatus.Abandoned"/> holds the claim.
    /// </returns>
    ValueTask<WorkflowResponseRecord?> FindClaimAsync(string claimScope, string requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Abandons an active record and writes the record of the run that replaces it, as one
    /// atomic step.
    /// </summary>
    /// <param name="abandonedResponseRunId">The run id of the record to abandon.</param>
    /// <param name="replacement">
    /// The record that takes over the claim. <see cref="WorkflowResponseRecord.Status"/> must be
    /// <see cref="WorkflowResponseStatus.Accepted"/>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the record was abandoned and <paramref name="replacement"/> written;
    /// <see langword="false"/> when the record is not found in the caller's tenant or is already
    /// <see cref="WorkflowResponseStatus.Abandoned"/>, in which case nothing is written.
    /// </returns>
    /// <remarks>
    /// Two concurrent calls that abandon the same record are decided by the store: one returns
    /// <see langword="true"/>, the other <see langword="false"/>.
    /// </remarks>
    ValueTask<bool> ReplaceAsync(Guid abandonedResponseRunId, WorkflowResponseRecord replacement, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the workflow received the answer: an <see cref="WorkflowResponseStatus.Accepted"/>
    /// record becomes <see cref="WorkflowResponseStatus.Delivered"/>.
    /// </summary>
    /// <param name="responseRunId">The run that delivered the answer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// Does nothing when no record of the caller's tenant has this run id, or when the record is
    /// no longer <see cref="WorkflowResponseStatus.Accepted"/>.
    /// </remarks>
    ValueTask MarkDeliveredAsync(Guid responseRunId, CancellationToken cancellationToken = default);

    /// <summary>Lists every answer given to a run's requests, oldest first.</summary>
    /// <param name="sourceRunId">The run that waited for the answers.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The records of the caller's tenant, ordered by <see cref="WorkflowResponseRecord.RespondedAt"/>.</returns>
    ValueTask<IReadOnlyList<WorkflowResponseRecord>> ListBySourceRunAsync(Guid sourceRunId, CancellationToken cancellationToken = default);

    /// <summary>Returns the record of one answering run.</summary>
    /// <param name="responseRunId">The run that answered.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The record, or <see langword="null"/> when it is not found in the caller's tenant.</returns>
    ValueTask<WorkflowResponseRecord?> GetAsync(Guid responseRunId, CancellationToken cancellationToken = default);
}

/// <summary>The state of a recorded answer.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkflowResponseStatus>))]
public enum WorkflowResponseStatus
{
    /// <summary>The answer was accepted and its run was started; the workflow has not reported receiving it.</summary>
    Accepted = 0,

    /// <summary>The run handed the answer to the workflow.</summary>
    Delivered = 1,

    /// <summary>
    /// The run stopped before it could finish with the answer, and the same answer started a
    /// new run. The record no longer holds a claim.
    /// </summary>
    Abandoned = 2,
}

/// <summary>One answer to a workflow's pending human request.</summary>
public sealed record WorkflowResponseRecord
{
    /// <summary>Gets the run the answer started. It identifies the record.</summary>
    public required Guid ResponseRunId { get; init; }

    /// <summary>Gets the run that waited for the answer.</summary>
    public required Guid SourceRunId { get; init; }

    /// <summary>Gets the execution session the two runs share.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the id of the request that was answered.</summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// Gets the SHA-256 of the answer, as 64 lowercase hexadecimal characters.
    /// </summary>
    /// <remarks>
    /// The hash tells two answers apart; it does not hide the answer. An answer with few
    /// possible values, such as a yes/no, can be recovered from it by trying each value.
    /// </remarks>
    public required string AnswerHash { get; init; }

    /// <summary>
    /// Gets the scope of the single-answer claim this record holds, or <see langword="null"/>
    /// for an answer to a <see cref="WorkflowResponseMode.Branch"/> workflow, which claims nothing.
    /// </summary>
    public string? ClaimScope { get; init; }

    /// <summary>Gets the state of the answer.</summary>
    public WorkflowResponseStatus Status { get; init; }

    /// <summary>Gets who answered, as the audit trail names the actor; <see langword="null"/> when unknown.</summary>
    public string? RespondedBy { get; init; }

    /// <summary>Gets when the answer was accepted (UTC).</summary>
    public required DateTimeOffset RespondedAt { get; init; }

    /// <summary>Gets the tenant the record belongs to. Set when it is written; not part of the HTTP response.</summary>
    [JsonIgnore]
    public string? TenantId { get; init; }
}

/// <summary>The outcome of recording an answer that may claim a request.</summary>
public sealed record WorkflowResponseClaim
{
    /// <summary>Gets a value indicating whether the record was written.</summary>
    public required bool Claimed { get; init; }

    /// <summary>
    /// Gets the active record that already held the claim when <see cref="Claimed"/> is
    /// <see langword="false"/>; otherwise <see langword="null"/>.
    /// </summary>
    public WorkflowResponseRecord? Existing { get; init; }
}
