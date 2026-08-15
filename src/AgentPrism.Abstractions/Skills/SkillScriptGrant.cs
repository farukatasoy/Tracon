namespace AgentPrism;

/// <summary>
/// The record carrying permission to execute a skill script.
/// </summary>
/// <remarks>
/// <para>
/// Script execution is a deliberate exception to AgentPrism's "tools are
/// defined only in code" rule. The grant record is this exception's gate: a
/// script with no grant record does not run, even with a whitelisted interpreter.
/// </para>
/// <para>
/// If <see cref="ScriptName"/> is <see langword="null"/>, the grant covers
/// <em>all</em> of the skill's scripts.
/// </para>
/// </remarks>
public sealed record SkillScriptGrant
{
    /// <summary>The grant record identifier. A time-ordered UUID (v7).</summary>
    public Guid Id { get; init; }

    /// <summary>The tenant the grant belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>The name of the skill granted.</summary>
    public required string SkillName { get; init; }

    /// <summary>
    /// The name of the script granted. If <see langword="null"/>, all of the
    /// skill's scripts are covered.
    /// </summary>
    public string? ScriptName { get; init; }

    /// <summary>The actor who gave the grant.</summary>
    public string? GrantedBy { get; init; }

    /// <summary>The moment the grant was given (UTC).</summary>
    public DateTimeOffset GrantedAt { get; init; }

    /// <summary>The moment the grant expires. Never expires if <see langword="null"/>.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>The moment the grant was revoked. Active if <see langword="null"/>.</summary>
    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>Reports whether the grant is active at the given moment.</summary>
    /// <param name="instant">The evaluation moment.</param>
    /// <returns><see langword="true"/> if the grant is active.</returns>
    /// <remarks>
    /// An expired grant is <strong>automatically</strong> invalid; the record
    /// is not expected to be deleted. Cleanup is the retention phase's concern.
    /// </remarks>
    public bool IsActiveAt(DateTimeOffset instant)
        => RevokedAt is null && (ExpiresAt is null || ExpiresAt > instant);
}
