namespace AgentPrism;

/// <summary>
/// Binds a tenant to a model provider's credential, by <strong>name</strong>
/// only.
/// </summary>
/// <remarks>
/// This record carries no secret value, only the name of the
/// configuration key the value is read from at call time.
/// applies to this store the same way it applies to MCP server credentials:
/// the database backup, the audit trail, and every HTTP response built from
/// this type never carry a secret.
/// </remarks>
public sealed record TenantProviderBinding
{
    /// <summary>Gets the tenant this binding belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the provider name. Matches <see cref="ModelBinding.Provider"/>.</summary>
    public required string ProviderName { get; init; }

    /// <summary>
    /// Gets the configuration <strong>key name</strong> the credential value is
    /// read from. Never the value itself.
    /// </summary>
    /// <remarks>
    /// Must start with <see cref="AgentPrismTenantProviderOptions.AllowedConfigurationPrefix"/>;
    /// resolution is rejected otherwise.
    /// </remarks>
    public required string ApiKeyConfigurationName { get; init; }

    /// <summary>Gets the optional provider endpoint override for this tenant.</summary>
    public string? Endpoint { get; init; }

    /// <summary>Gets the time this binding was last written.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
