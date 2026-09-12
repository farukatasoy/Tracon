namespace Tracon;

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
    /// <remarks>
    /// Compared <strong>case-insensitively</strong>, like every other place a
    /// provider name is matched (the provider registry, the tenant egress
    /// policy, the admin endpoint). A store persists the value
    /// <see cref="NormalizeProviderName"/> returns, so <c>"OpenAI"</c> and
    /// <c>"openai"</c> are one binding and never two.
    /// </remarks>
    public required string ProviderName { get; init; }

    /// <summary>
    /// Returns the canonical form a store persists and looks a provider name
    /// up by.
    /// </summary>
    /// <param name="providerName">The provider name as written by the caller.</param>
    /// <returns>The canonical (invariant lower-case) form.</returns>
    /// <remarks>
    /// <para>
    /// An <see cref="ITenantProviderBindingStore"/> implementation must apply
    /// this to the name it writes <em>and</em> to the name it is queried with.
    /// A store that skips it is case-sensitive while the rest of the system is
    /// not, and a tenant whose binding was saved under a different letter case
    /// silently falls through to the global setup credential — the wrong
    /// tenant gets billed and no error is raised.
    /// </para>
    /// <para>
    /// Normalizing the stored <em>value</em> (rather than comparing
    /// case-insensitively at query time) is deliberate: a bare <c>=</c>
    /// predicate then behaves identically on PostgreSQL, SQLite and SQL Server
    /// whatever their collation, the primary key keeps rejecting duplicates on
    /// all three, and the index stays usable.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="providerName"/> is null or blank.</exception>
    public static string NormalizeProviderName(string providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        return providerName.ToLowerInvariant();
    }

    /// <summary>
    /// Gets the configuration <strong>key name</strong> the credential value is
    /// read from. Never the value itself.
    /// </summary>
    /// <remarks>
    /// Must start with <see cref="TraconTenantProviderOptions.AllowedConfigurationPrefix"/>;
    /// resolution is rejected otherwise.
    /// </remarks>
    public required string ApiKeyConfigurationName { get; init; }

    /// <summary>Gets the optional provider endpoint override for this tenant.</summary>
    public string? Endpoint { get; init; }

    /// <summary>Gets the time this binding was last written.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
