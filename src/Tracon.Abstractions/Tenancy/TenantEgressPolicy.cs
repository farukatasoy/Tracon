namespace Tracon;

/// <summary>
/// The set of model providers a tenant's agents are allowed to call
/// (egress policy).
/// </summary>
/// <remarks>
/// A tenant with no saved policy is <strong>unrestricted</strong>: the
/// absence of a row, not an empty list, means "no limit". Once a policy is
/// saved, <see cref="AllowedProviders"/> is the closed set of providers the
/// tenant's agent definitions may name in <see cref="ModelBinding.Provider"/>.
/// </remarks>
public sealed record TenantEgressPolicy
{
    /// <summary>Gets the tenant this policy belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the closed set of provider names this tenant's agents may call.</summary>
    public required IReadOnlyList<string> AllowedProviders { get; init; }

    /// <summary>Gets the time this policy was last written.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
