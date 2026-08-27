namespace AgentPrism;

/// <summary>The criteria that scope a hash chain verification.</summary>
public sealed record AuditChainQuery
{
    /// <summary>
    /// Gets the tenant id. AMBIENT fallback: the caller's OWN
    /// <see cref="ITenantContext.TenantId"/> is used when this is
    /// <see langword="null"/> or empty — never "every tenant". See
    /// <see cref="IAuditLog"/>'s remarks for the full contract.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the lower bound: entries written at or after this time are checked.</summary>
    public DateTimeOffset? After { get; init; }

    /// <summary>Gets the upper bound: entries written at or before this time are checked.</summary>
    public DateTimeOffset? Before { get; init; }
}
