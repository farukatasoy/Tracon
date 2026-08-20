using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AgentPrism;

/// <summary>The scope an endpoint requires when it is called with an API key.</summary>
/// <param name="Scope">The required scope.</param>
/// <remarks>
/// This does NOT replace the role policies; it narrows API-key-authenticated requests IN
/// ADDITION to them. Requests that arrive with a static bearer token or with
/// a user identity are unaffected by this check — it applies only when a record exists in
/// <see cref="ApiKeyRequestContext"/>.
/// </remarks>
internal sealed record ApiKeyScopeRequirement(ApiKeyScope Scope);

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
    /// <returns>The same builder, so calls can be chained.</returns>
    public static TBuilder RequireApiKeyScope<TBuilder>(this TBuilder builder, ApiKeyScope scope)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new ApiKeyScopeRequirement(scope));

        return builder;
    }
}
