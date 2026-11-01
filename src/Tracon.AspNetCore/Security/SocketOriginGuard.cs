using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Tracon;

/// <summary>
/// Decides whether a WebSocket handshake that an authorization policy authenticates was
/// opened by a page the installation trusts.
/// </summary>
/// <remarks>
/// <para>
/// A browser attaches its cookies to a WebSocket handshake whichever page opens it, and a
/// WebSocket is not bound by CORS: no preflight asks the server, and the page reads every
/// frame. When a cookie authenticates the socket, the credential does not prove the page is
/// the console. The <c>Origin</c> header does: a browser sets it, and a page cannot change it.
/// </para>
/// <para>
/// An origin passes when it is the request's own origin or one of
/// <see cref="TraconEndpointOptions.AllowedOrigins"/>, the origins the installation already
/// trusts to call it from a browser. A handshake with no <c>Origin</c> header does not come
/// from a browser page (a server, a mobile app) and passes. A socket that presents its own
/// token or key is not judged here: a page of another site does not hold it.
/// </para>
/// </remarks>
internal static class SocketOriginGuard
{
    /// <summary>Returns the rejection for a handshake from a foreign page, or <see langword="null"/> when it may pass.</summary>
    /// <param name="httpContext">The handshake request. It presented no token or key of its own.</param>
    /// <param name="allowedOrigins">The origins the installation trusts, read when the endpoints were mapped.</param>
    /// <returns><c>403</c> for a foreign origin, <see langword="null"/> otherwise.</returns>
    public static ProblemHttpResult? Check(HttpContext httpContext, IReadOnlyCollection<string> allowedOrigins)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(allowedOrigins);

        if (httpContext.Request.Headers.Origin.ToString() is not { Length: > 0 } origin
            || IsOwnOrigin(httpContext.Request, origin)
            || IsAllowed(origin, allowedOrigins))
        {
            return null;
        }

        return TypedResults.Problem(
            title: "Origin not allowed",
            detail: "The page that opened this socket is not served by this installation, and its origin is not " +
                    $"in {nameof(TraconEndpointOptions)}.{nameof(TraconEndpointOptions.AllowedOrigins)}. A browser " +
                    "sends its cookies on a socket from any page, so the socket is refused.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    // The scheme is not compared: behind a proxy that ends TLS the request says http while the
    // page says https, and the cookie's own Secure flag already keeps the two apart.
    private static bool IsOwnOrigin(HttpRequest request, string origin)
    {
        if (!request.Host.HasValue
            || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)))
        {
            return false;
        }

        if (!string.Equals(uri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // A Host header without a port names the default port of whatever scheme the page used.
        return request.Host.Port is { } port ? port == uri.Port : uri.IsDefaultPort;
    }

    private static bool IsAllowed(string origin, IReadOnlyCollection<string> allowedOrigins)
    {
        var presented = origin.TrimEnd('/');

        foreach (var allowed in allowedOrigins)
        {
            if (string.Equals(presented, allowed.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
