namespace Tracon;

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

    /// <summary>
    /// Gets the number of walked entries that carry no signing key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A chain that has signed every entry since its first has <c>0</c> here. A chain
    /// that switched the key on later counts the entries written before the switch: they
    /// are checked with the plain hash they were written with.
    /// </para>
    /// <para>
    /// Read this number together with <see cref="Status"/>. An actor who can write to the
    /// audit table but cannot read the key can still turn signed entries into unsigned ones
    /// when EVERY entry from the first signed one onward is rewritten; the walk then
    /// reports <c>Valid</c> and this count rises.
    /// </para>
    /// </remarks>
    public int UnkeyedEntries { get; init; }
}
