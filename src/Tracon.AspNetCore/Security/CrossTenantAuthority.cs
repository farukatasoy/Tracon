using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Decides whether the caller may act on a tenant other than its own.
/// </summary>
/// <remarks>
/// <para>
/// Most endpoints never face this question: they act on the AMBIENT tenant
/// (<see cref="ITenantContext"/>), which is the caller's own. A few take the
/// tenant from the request — <c>/api/tenants/{tenantId}/providers</c>,
/// <c>/egress</c> and the tenant records — because an operator of the
/// installation manages every tenant through them. Nothing enforced
/// that the caller WAS such an operator: a key bound to tenant A with
/// <c>SecurityAdmin</c>, or a claims-based Admin of tenant A, could point
/// tenant B's provider binding at an address A controls.
/// </para>
/// <para>
/// Platform authority per identity, in the order the request proves it:
/// </para>
/// <list type="number">
/// <item><description>An API key needs the <see cref="ApiKeyScope.PlatformAdmin"/> scope.</description></item>
/// <item><description>The static <see cref="TraconEndpointOptions.AuthToken"/> identifies the installation: it has it.</description></item>
/// <item><description>
/// A claims principal needs the <see cref="TraconPolicies.PlatformAdmin"/> policy
/// while multi-tenancy is on; a policy that is not registered DENIES. With
/// multi-tenancy off a claim resolves no tenant, so there is no "other tenant"
/// for it to reach.
/// </description></item>
/// <item><description>
/// An anonymous caller is the zero-configuration local operator: with no
/// authentication configured, only the loopback interface reaches it. It has it.
/// </description></item>
/// </list>
/// </remarks>
internal static class CrossTenantAuthority
{
    /// <summary>
    /// Returns a <c>403</c> when the caller may not act on <paramref name="targetTenantId"/>.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="targetTenantId">
    /// The canonical tenant the request acts on, or <see langword="null"/> when it
    /// acts on every tenant of the installation (a listing).
    /// </param>
    /// <returns>The rejection; <see langword="null"/> when the caller may proceed.</returns>
    public static async ValueTask<ProblemHttpResult?> CheckAsync(HttpContext httpContext, string? targetTenantId)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var services = httpContext.RequestServices;

        if (targetTenantId is not null
            && string.Equals(services.GetRequiredService<ITenantContext>().TenantId, targetTenantId, StringComparison.Ordinal))
        {
            return null;
        }

        if (ApiKeyRequestContext.Get(httpContext) is { } key)
        {
            return key.Scopes.Contains(ApiKeyScope.PlatformAdmin)
                ? null
                : Denied(
                    $"This API key is bound to tenant '{key.TenantId}'. Acting on another tenant " +
                    $"requires a key that also carries the '{ApiKeyScope.PlatformAdmin}' scope.");
        }

        if (StaticTokenRequestContext.IsSet(httpContext))
        {
            return null;
        }

        if (httpContext.User.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        if (services.GetService<IOptions<TraconTenancyOptions>>()?.Value.Enabled != true)
        {
            return null;
        }

        var policy = services.GetService<IAuthorizationPolicyProvider>() is { } provider
            ? await provider.GetPolicyAsync(TraconPolicies.PlatformAdmin).ConfigureAwait(false)
            : null;

        if (policy is null)
        {
            return Denied(
                $"Acting on another tenant requires the '{TraconPolicies.PlatformAdmin}' authorization " +
                "policy, and it is not registered. Define it inside " +
                "builder.Services.AddAuthorization(...) for the operators of the installation.");
        }

        var result = await services
            .GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(httpContext.User, resource: null, policy)
            .ConfigureAwait(false);

        return result.Succeeded
            ? null
            : Denied($"Acting on another tenant requires the '{TraconPolicies.PlatformAdmin}' policy.");
    }

    private static ProblemHttpResult Denied(string detail)
        => TypedResults.Problem(
            title: "Platform authority required",
            detail: detail,
            statusCode: StatusCodes.Status403Forbidden);
}

/// <summary>Attaches <see cref="CrossTenantAuthority"/> to an endpoint.</summary>
internal static class CrossTenantAuthorityEndpointConventionBuilderExtensions
{
    /// <summary>
    /// Requires platform authority when the tenant in route parameter
    /// <paramref name="routeParameterName"/> is not the caller's own.
    /// </summary>
    /// <param name="builder">The endpoint builder.</param>
    /// <param name="routeParameterName">The route parameter that names the tenant.</param>
    /// <returns>The same builder, so calls can be chained.</returns>
    /// <remarks>
    /// Runs as an endpoint filter, so it runs AFTER the group's
    /// <c>TraconEndpointFilter</c>: the API key and the static token are
    /// already recognized when it asks who the caller is.
    /// </remarks>
    public static RouteHandlerBuilder RequirePlatformAuthorityForOtherTenant(
        this RouteHandlerBuilder builder,
        string routeParameterName)
        => builder.AddEndpointFilter(async (context, next) =>
        {
            var routeValue = context.HttpContext.Request.RouteValues[routeParameterName]?.ToString() ?? string.Empty;
            var denied = await CrossTenantAuthority
                .CheckAsync(context.HttpContext, HttpTenantContext.NormalizeRouteTenantId(routeValue))
                .ConfigureAwait(false);

            return denied ?? await next(context).ConfigureAwait(false);
        });

    /// <summary>Requires platform authority: the endpoint acts on every tenant of the installation.</summary>
    /// <param name="builder">The endpoint builder.</param>
    /// <returns>The same builder, so calls can be chained.</returns>
    public static RouteHandlerBuilder RequirePlatformAuthority(this RouteHandlerBuilder builder)
        => builder.AddEndpointFilter(async (context, next) =>
        {
            var denied = await CrossTenantAuthority.CheckAsync(context.HttpContext, targetTenantId: null).ConfigureAwait(false);

            return denied ?? await next(context).ConfigureAwait(false);
        });
}
