namespace AgentPrism;

/// <summary>
/// The pending tool approval request of a run that executes from the queue.
/// </summary>
/// <remarks>
/// <para>
/// This record is a <strong>projection</strong>, not the owner: the single source of
/// truth is the session state of MAF (<c>ToolApprovalRequestContent</c>, which lives in
/// the session history). While the decision is applied the session is read, not this table.
/// </para>
/// <para>
/// The run that owns the pending request closes with
/// <see cref="RunStatus.AwaitingApproval"/> and never changes again. Once the
/// decision is made the SAME run does not continue; a <strong>new</strong> run is put on
/// the queue (the same <see cref="SessionId"/>, a new <c>RunId</c>).
/// </para>
/// </remarks>
public sealed record PendingApproval
{
    /// <summary>Gets the record id.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the tenant id.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the run that produced this request and closed with <see cref="RunStatus.AwaitingApproval"/>.</summary>
    public required Guid RunId { get; init; }

    /// <summary>
    /// Gets the session of the run. While the decision is applied, the continuing run is
    /// put on the queue with this session.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the <c>ToolApprovalRequestContent.RequestId</c> value produced by MAF.</summary>
    public required string RequestId { get; init; }

    /// <summary>Gets the name of the tool that asks for approval.</summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Gets the arguments of the tool call as <c>key=value</c> pairs (reflection-based
    /// JSON serialization is NOT USED, to stay AOT compatible). It stays
    /// <see langword="null"/> when <c>RecordToolPayloads</c> is turned off in the
    /// recording settings.
    /// </summary>
    public string? Arguments { get; init; }

    /// <summary>Gets the status of the request.</summary>
    public required ApprovalStatus Status { get; init; }

    /// <summary>Gets the actor that made the decision, or <see langword="null"/> when no decision was made.</summary>
    public string? DecidedBy { get; init; }

    /// <summary>Gets the time of the decision, or <see langword="null"/> when no decision was made.</summary>
    public DateTimeOffset? DecidedAt { get; init; }

    /// <summary>
    /// Gets the time after which the request counts as <see cref="ApprovalStatus.Expired"/>.
    /// </summary>
    /// <remarks>It is required: an approval request that waits forever is a leak.</remarks>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Gets the creation time.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
