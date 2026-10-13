namespace Tracon;

/// <summary>
/// The set of providers a tenant's data is allowed to reach (egress policy).
/// </summary>
/// <remarks>
/// <para>
/// A tenant with no saved policy is <strong>unrestricted</strong>: the
/// absence of a row, not an empty list, means "no limit". Once a policy is
/// saved, <see cref="AllowedProviders"/> is the closed set of providers the
/// tenant's calls may reach.
/// </para>
/// <para>
/// One name space covers every kind of provider: a model provider
/// (<see cref="ModelBinding.Provider"/>), a speech provider
/// (<see cref="ISpeechSynthesizer.ProviderName"/>,
/// <see cref="ISpeechTranscriber.ProviderName"/>), a live voice provider and the
/// image provider. A provider the list does not name is refused before the call.
/// </para>
/// </remarks>
public sealed record TenantEgressPolicy
{
    /// <summary>Gets the tenant this policy belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the closed set of provider names this tenant's calls may reach.</summary>
    public required IReadOnlyList<string> AllowedProviders { get; init; }

    /// <summary>Gets the time this policy was last written.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
