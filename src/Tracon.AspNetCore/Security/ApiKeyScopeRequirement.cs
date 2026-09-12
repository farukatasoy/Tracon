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
internal sealed record ApiKeyScopeRequirement(ApiKeyScope Scope, bool Mandatory = false);

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
}
