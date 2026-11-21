using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Tracon;

/// <summary>The scope an endpoint requires when it is called with an API key.</summary>
/// <param name="Scope">The required scope.</param>
/// <param name="Mandatory">
/// When <see langword="false"/> (the default) this narrows API-key-authenticated
/// requests IN ADDITION to the role policies: a caller that presents no API key
/// is unaffected, which is what keeps the loopback default zero-configuration.
/// When <see langword="true"/> the endpoint cannot be reached WITHOUT an API key
/// carrying the scope, once the surface is reachable beyond loopback. Only the
/// externally exposed agent surfaces (MCP, A2A) set this.
/// </param>
/// <remarks>
/// A mandatory requirement is what makes <c>ExternalSurfaceGuard</c>'s promise
/// true at request time. That guard only checks at STARTUP that a key with the
/// scope exists somewhere in the installation; without this flag nothing then
/// forced a caller to actually present it, and an agent surface published beyond
/// loopback answered unauthenticated requests.
/// </remarks>
internal sealed record ApiKeyScopeRequirement(ApiKeyScope Scope, bool Mandatory = false)
{
    /// <summary>
    /// Gets the scopes that open the endpoint as well as <see cref="Scope"/>. Empty for the usual endpoint, which needs
    /// exactly one scope.
    /// </summary>
    /// <remarks>
    /// The AG-UI endpoint is the one that sets it: a key made for a chat front end carries the narrow chat scope, and the
    /// keys that were made for it before that scope existed carry the run scope.
    /// </remarks>
    public IReadOnlyList<ApiKeyScope> AlsoAccepted { get; init; } = [];

    /// <summary>Gets whether a key that holds <paramref name="granted"/> opens the endpoint.</summary>
    /// <param name="granted">The scopes of the key.</param>
    /// <returns><see langword="true"/> when the key holds <see cref="Scope"/> or one of <see cref="AlsoAccepted"/>.</returns>
    public bool IsMetBy(IReadOnlyCollection<ApiKeyScope> granted)
    {
        ArgumentNullException.ThrowIfNull(granted);

        return granted.Contains(Scope) || AlsoAccepted.Any(granted.Contains);
    }

    /// <summary>Names the scope or scopes the endpoint asks for, for a refusal's text.</summary>
    /// <returns>The scope names, quoted, joined with <c>or</c>.</returns>
    public string Describe()
        => string.Join(" or ", new[] { Scope }.Concat(AlsoAccepted).Select(static scope => $"'{scope}'"));
}

/// <summary>Extension that adds a scope requirement to an endpoint convention.</summary>
internal static class ApiKeyScopeEndpointConventionBuilderExtensions
{
    /// <summary>
    /// Marks that the endpoint requires <paramref name="scope"/> before it can be called
    /// with an API key.
    /// </summary>
    /// <typeparam name="TBuilder">The convention builder type.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="scope">The required scope.</param>
    /// <param name="mandatory">
    /// <see langword="true"/> when the endpoint may not be reached without an API
    /// key carrying the scope. See <see cref="ApiKeyScopeRequirement.Mandatory"/>.
    /// </param>
    /// <returns>The same builder, so calls can be chained.</returns>
    public static TBuilder RequireApiKeyScope<TBuilder>(this TBuilder builder, ApiKeyScope scope, bool mandatory = false)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new ApiKeyScopeRequirement(scope, mandatory));

        return builder;
    }

    /// <summary>
    /// Marks that the endpoint requires <paramref name="scope"/> or <paramref name="alsoAccepted"/> before it can be
    /// called with an API key: a key that carries either one opens it.
    /// </summary>
    /// <typeparam name="TBuilder">The convention builder type.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="scope">The scope the endpoint is made for.</param>
    /// <param name="alsoAccepted">A second scope that opens it as well.</param>
    /// <returns>The same builder, so calls can be chained.</returns>
    public static TBuilder RequireAnyApiKeyScope<TBuilder>(this TBuilder builder, ApiKeyScope scope, ApiKeyScope alsoAccepted)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new ApiKeyScopeRequirement(scope) { AlsoAccepted = [alsoAccepted] });

        return builder;
    }
}
