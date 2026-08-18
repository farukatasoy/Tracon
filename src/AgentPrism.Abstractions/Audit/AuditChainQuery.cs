namespace AgentPrism;

/// <summary>The criteria that scope a hash chain verification.</summary>
public sealed record AuditChainQuery
{
    /// <summary>Gets the tenant id. The tenant of the caller is used when it is <see langword="null"/>.</summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the lower bound: entries written at or after this time are checked.</summary>
    public DateTimeOffset? After { get; init; }

    /// <summary>Gets the upper bound: entries written at or before this time are checked.</summary>
    public DateTimeOffset? Before { get; init; }
}
