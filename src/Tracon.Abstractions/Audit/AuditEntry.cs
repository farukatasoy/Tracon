namespace Tracon;

/// <summary>
/// A row in the audit trail that records who changed which entity and when.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Before"/> and <see cref="After"/> pass through a secret filter before they
/// are written: the values of the <c>apiKey</c>, <c>authorization</c>, <c>token</c>,
/// <c>password</c> and <c>secret</c> keys are replaced with <c>"***"</c>.
/// </para>
/// <para>
/// Runs (an agent processing a message) are <strong>not written</strong> to this trail.
/// The <c>runs</c> table already holds the full record; writing it a second time would
/// turn the audit trail into the largest table and make it unreadable.
/// </para>
/// </remarks>
public sealed record AuditEntry
{
    /// <summary>Gets the record id. A time-ordered UUID (v7).</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the tenant the change belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Gets the actor that made the change. It is <see langword="null"/> when there is no
    /// authentication or the actor cannot be resolved; that state is not hidden.
    /// </summary>
    public string? Actor { get; init; }

    /// <summary>Gets the action name, for example <c>agent.update</c>.</summary>
    public required string Action { get; init; }

    /// <summary>Gets the affected entity, for example <c>agent:support</c>.</summary>
    public required string Entity { get; init; }

    /// <summary>
    /// Gets the state before the change, as JSON text. Tracon's own write path
    /// removes secret-named fields first; a caller that writes to
    /// <see cref="IAuditLog"/> directly must not put a secret value here.
    /// </summary>
    public string? Before { get; init; }

    /// <summary>
    /// Gets the state after the change, as JSON text. Tracon's own write path
    /// removes secret-named fields first; a caller that writes to
    /// <see cref="IAuditLog"/> directly must not put a secret value here.
    /// </summary>
    public string? After { get; init; }

    /// <summary>Gets the time the record was written (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets the hash of the previous entry of the same tenant.
    /// <see langword="null"/> for the first entry of a tenant.
    /// </summary>
    /// <remarks>
    /// The chain is per <c>TenantId</c>: one tenant's write rate never waits on
    /// another tenant's chain. The write path computes this value; a caller-supplied
    /// value is ignored.
    /// </remarks>
    public string? PreviousHash { get; init; }

    /// <summary>
    /// Gets the hash of this entry: a SHA-256 over a canonical form of its
    /// fields that also includes the previous entry's hash, so changing an
    /// earlier entry breaks every later link. The write path computes this
    /// value; a caller-supplied value is ignored.
    /// </summary>
    public string? Hash { get; init; }
}
