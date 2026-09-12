namespace Tracon;

/// <summary>The criteria that filter an audit trail query.</summary>
public sealed record AuditQuery
{
    /// <summary>
    /// Gets the tenant id. AMBIENT fallback: the caller's OWN
    /// <see cref="ITenantContext.TenantId"/> is used when this is
    /// <see langword="null"/> or empty — never "every tenant". See
    /// <see cref="IAuditLog"/>'s remarks for the full contract.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the actor filter. <see langword="null"/> includes every actor.</summary>
    public string? Actor { get; init; }

    /// <summary>Gets the action filter, for example <c>agent.update</c>.</summary>
    public string? Action { get; init; }

    /// <summary>Gets the entity filter, for example <c>agent:support</c>.</summary>
    public string? Entity { get; init; }

    /// <summary>Gets the lower bound: the records written after this time.</summary>
    public DateTimeOffset? After { get; init; }

    /// <summary>Gets the upper bound: the records written before this time.</summary>
    public DateTimeOffset? Before { get; init; }

    /// <summary>Gets the upper number of records to return.</summary>
    public int Limit { get; init; } = 100;
}
