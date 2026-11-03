namespace Tracon;

/// <summary>
/// A resolved, in-memory credential used to call a provider (a model, live voice or
/// speech provider) on behalf of a specific tenant.
/// </summary>
/// <remarks>
/// This type carries the actual secret <em>value</em>. It is produced only
/// at call time, by resolving a <see cref="TenantProviderBinding"/>'s
/// configuration key through the application's configuration system; it is
/// never constructed from a database row and never persisted anywhere.
/// If a provider's error text quotes <see cref="ApiKey"/> back, Tracon replaces the key
/// with <c>[redacted]</c> before that text reaches a log line or a span status. The match
/// is exact: a part of the key the provider quotes on its own is left as the provider wrote it.
/// </remarks>
public sealed record ModelProviderCredential
{
    /// <summary>Gets the resolved API key. Never persisted; lives only in memory.</summary>
    public required string ApiKey { get; init; }

    /// <summary>Gets the optional provider endpoint override.</summary>
    public string? Endpoint { get; init; }

    /// <summary>Returns a description that carries no secret value.</summary>
    /// <returns>The type name and the endpoint; never the key.</returns>
    /// <remarks>
    /// A record's compiler-generated <c>ToString</c> prints every property, so a
    /// single <c>LogDebug("{Credential}", credential)</c> would put a live
    /// provider key in the log. The override removes that possibility instead of
    /// relying on nobody ever writing that line.
    /// </remarks>
    public override string ToString()
        => $"{nameof(ModelProviderCredential)} {{ ApiKey = [redacted], Endpoint = {Endpoint} }}";
}
