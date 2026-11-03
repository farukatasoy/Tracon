using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Tracon;

/// <summary>
/// Marks an endpoint whose route carries a tenant segment and answers <c>400</c> for a value outside the tenant
/// alphabet before its handler or its authority check runs.
/// </summary>
/// <param name="routeParameterName">The route parameter that names the tenant.</param>
/// <remarks>
/// Attached by <see cref="CrossTenantAuthorityEndpointConventionBuilderExtensions"/> and read by the test that
/// walks the endpoint table, so a new route with a tenant segment cannot ship without the check.
/// </remarks>
internal sealed class TenantRouteValidationMetadata(string routeParameterName)
{
    /// <summary>Gets the route parameter that names the tenant.</summary>
    public string RouteParameterName { get; } = routeParameterName;
}

/// <summary>
/// Judges the tenant segment of a route before any store sees it.
/// </summary>
/// <remarks>
/// A route segment is the client's text. <see cref="AmbientTenantScope.Normalize"/> refuses a character outside
/// the tenant alphabet because every store applies it, so a segment that reached a store unchecked would fail
/// the request with <c>500</c>. The response never repeats the value.
/// </remarks>
internal static class TenantRouteValidation
{
    /// <summary>Checks the tenant segment of a route.</summary>
    /// <param name="routeValue">The raw route value.</param>
    /// <returns>
    /// <see langword="null"/> when the value is a tenant identifier or is blank; a <c>400</c> problem otherwise.
    /// </returns>
    /// <remarks>
    /// A blank segment (white space only) is not judged here: it names no tenant, matches no row, and keeps the answer
    /// the blank-route middleware and the handlers have always given for it.
    /// </remarks>
    public static ProblemHttpResult? Check(string? routeValue)
        => string.IsNullOrWhiteSpace(routeValue) || AmbientTenantScope.TryNormalize(routeValue, out _)
            ? null
            : TypedResults.Problem(
                title: "Tenant key invalid",
                detail: "Invalid tenant key. The key must contain only letters, digits, dots, underscores, and hyphens.",
                statusCode: StatusCodes.Status400BadRequest);
}
