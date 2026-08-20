namespace AgentPrism;

/// <summary>
/// A resolved, in-memory credential used to call a model provider on behalf
/// of a specific tenant.
/// </summary>
/// <remarks>
/// This type carries the actual secret <em>value</em>. It is produced only
/// at call time, by resolving a <see cref="TenantProviderBinding"/>'s
/// configuration key through the application's configuration system; it is
/// never constructed from a database row and never persisted anywhere.
/// </remarks>
public sealed record ModelProviderCredential
{
    /// <summary>Gets the resolved API key. Never persisted; lives only in memory.</summary>
    public required string ApiKey { get; init; }

    /// <summary>Gets the optional provider endpoint override.</summary>
    public string? Endpoint { get; init; }
}
