namespace AgentPrism;

/// <summary>The result of verifying one tenant's audit trail hash chain.</summary>
public sealed record AuditChainVerification
{
    /// <summary>Gets the overall chain status.</summary>
    public required AuditChainStatus Status { get; init; }

    /// <summary>Gets the number of entries walked.</summary>
    public required int EntriesChecked { get; init; }

    /// <summary>
    /// Gets the id of the first entry where the chain fails; <see langword="null"/>
    /// when <see cref="Status"/> is <c>AuditChainStatus.Valid</c>.
    /// </summary>
    public Guid? FirstFailingEntryId { get; init; }
}
