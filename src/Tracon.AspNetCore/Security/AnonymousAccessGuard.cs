using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Tracon;

/// <summary>
/// Decides whether a request that presented no credential may pass on the
/// zero-configuration default.
/// </summary>
/// <remarks>
/// <para>
/// With no <see cref="TraconEndpointOptions.AuthToken"/> and no authorization
/// policy, an anonymous caller is treated as the local operator. That promise
/// holds only for a caller that is really on this machine and is not a browser
/// page of another site. Three holes followed from not checking it:
/// </para>
/// <list type="number">
/// <item><description>
/// <see cref="TraconEndpointOptions.AllowRemoteAccess"/> on, with no
/// authentication method, admitted every remote request with full rights.
/// </description></item>
/// <item><description>
/// A reverse proxy on the same machine made every forwarded request look
/// local (see <see cref="LoopbackGuard.IsLocalRequest"/>).
/// </description></item>
/// <item><description>
/// A page on another site reached the local API through DNS rebinding (its
/// own name in <c>Host</c>) or a cross-site request (its own <c>Origin</c>).
/// A WebSocket is not bound by CORS, so the voice socket was open to it too.
/// </description></item>
/// </list>
/// <para>
/// A request with a credential is not judged here: a page of another site
/// cannot present the token or the key. A configured authorization policy
/// decides for itself.
/// </para>
/// </remarks>
internal static class AnonymousAccessGuard
{
    /// <summary>Returns the rejection for an anonymous request, or <see langword="null"/> when it may pass.</summary>
    /// <param name="httpContext">The current request. It presented no credential, and no static token is configured.</param>
    /// <param name="authorizationPolicyConfigured">
    /// Whether <see cref="TraconEndpointOptions.RequireAuthorization"/> set a policy,
    /// read when the endpoints were mapped.
    /// </param>
    /// <returns>
    /// <c>401</c> for a remote request, <c>403</c> for a foreign host or origin,
    /// <see langword="null"/> otherwise.
    /// </returns>
    public static ProblemHttpResult? Check(HttpContext httpContext, bool authorizationPolicyConfigured)
    {
        if (authorizationPolicyConfigured)
        {
            return null;
        }

        if (!LoopbackGuard.IsLocalRequest(httpContext))
        {
            // Reached only with AllowRemoteAccess on: the loopback restriction
            // already refused a remote request while it is off.
            httpContext.Response.Headers.WWWAuthenticate = "Bearer";

            return TypedResults.Problem(
                title: "Authentication required",
                detail: "Remote access is on, but no authentication method is configured, so an " +
                        "anonymous request is accepted only from this machine. Configure " +
                        $"{nameof(TraconEndpointOptions.AuthToken)} or " +
                        $"{nameof(TraconEndpointOptions.RequireAuthorization)}, or send an API key " +
                        "as 'Authorization: Bearer <key>'.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (httpContext.Request.Host.HasValue && !LoopbackGuard.IsLoopbackHost(httpContext.Request.Host.Host))
        {
            return TypedResults.Problem(
                title: "Host not allowed",
                detail: "An anonymous request is accepted only for a loopback host name " +
                        "('localhost', '*.localhost', '127.0.0.1', '[::1]'). Another name that " +
                        "resolves to this machine is how a web page takes over a local service. " +
                        "Use a loopback name, or configure an authentication method.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (httpContext.Request.Headers.Origin.ToString() is { Length: > 0 } origin
            && !LoopbackGuard.IsLoopbackOrigin(origin))
        {
            return TypedResults.Problem(
                title: "Origin not allowed",
                detail: "An anonymous request from a web page is accepted only from a page served " +
                        "by this machine. Configure an authentication method to call the API " +
                        "from another origin.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return null;
    }
}
