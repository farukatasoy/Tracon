using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;

namespace AgentPrism;

/// <summary>
/// Provides the conditional way to add role-based authorization to an endpoint.
/// </summary>
internal static class RoleEndpointConventionBuilderExtensions
{
    /// <summary>
    /// Adds <c>RequireAuthorization(policyName)</c> to the endpoint when
    /// <paramref name="policyName"/> is not <see langword="null"/>; does nothing when it is
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// A <see langword="null"/> <paramref name="policyName"/> means that the matching
    /// <see cref="AgentPrismPolicies"/> policy is not registered in the authorization
    /// configuration of the consumer — in that case the endpoint passes only the existing
    /// three-layer protection (loopback, bearer, general policy).
    /// </remarks>
    /// <param name="builder">The endpoint builder.</param>
    /// <param name="policyName">The policy name to apply; <see langword="null"/> when it is not registered.</param>
    /// <returns>The builder, unchanged.</returns>
    public static TBuilder RequireRole<TBuilder>(this TBuilder builder, string? policyName)
        where TBuilder : IEndpointConventionBuilder
    {
        if (policyName is not null)
        {
            builder.RequireAuthorization(policyName);
        }

        return builder;
    }
}
