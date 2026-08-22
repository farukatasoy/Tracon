namespace AgentPrism.Client;

/// <summary>Settings the typed management client is built from.</summary>
/// <remarks>
/// A plain <see langword="class"/>, not a <see langword="record"/>: a record's
/// compiler-generated <c>ToString</c> would print <see cref="Token"/> in any
/// log statement or exception message that includes the options instance.
/// </remarks>
public sealed class AgentPrismClientOptions
{
    /// <summary>
    /// The application root plus the <c>MapAgentPrism</c> prefix, for example
    /// <c>https://example.com/agentprism/</c> for the default prefix, or
    /// <c>https://example.com/control/</c> for an app that called
    /// <c>MapAgentPrism("/control")</c>.
    /// </summary>
    /// <remarks>
    /// The document every operation is generated from carries no prefix: the
    /// client sends requests relative to this address, so it must end with
    /// <c>/</c> or the last path segment is dropped when the relative URI is
    /// resolved (<see cref="Uri(Uri, string)"/> semantics).
    /// <c>AddAgentPrismClient</c> appends a trailing <c>/</c> automatically
    /// when one is missing.
    /// </remarks>
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// The bearer token sent as an <c>Authorization</c> header. Never written
    /// to a file or a database: pass it from an environment variable or a
    /// secret store, not a literal.
    /// </summary>
    public string? Token { get; set; }
}
