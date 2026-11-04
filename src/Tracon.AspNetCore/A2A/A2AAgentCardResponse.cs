using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using A2A;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Tracon;

/// <summary>
/// Writes the agent card with an interface address a caller outside the host can use.
/// </summary>
/// <remarks>
/// <para>
/// The card says where the interface is, and a client calls what the card says. A relative path is only an address to a
/// client that resolves it against the card's own URL; the official JavaScript SDK builds a <see cref="Uri"/> from the
/// value as written and throws on a path. The address is therefore absolute, built from what the request says about how
/// it arrived: <c>Request.Scheme</c>, <c>Request.Host</c> and <c>Request.PathBase</c>.
/// </para>
/// <para>
/// Nothing here reads <c>X-Forwarded-Host</c> or <c>X-Forwarded-Proto</c>. A host that runs the forwarded headers
/// middleware has the framework rewrite those three values before the endpoint sees them, from the proxies it trusts; a
/// host that does not gets the address of the connection, and a header a client wrote changes nothing. The host name is
/// still what the caller put in the <c>Host</c> header, so an application that is reachable under any name restricts the
/// names it answers to (<c>AllowedHosts</c>), as it does for every other absolute address it builds.
/// </para>
/// <para>
/// The card differs by request now, so it is built per request, and the response names the request headers the address
/// depends on (<c>Vary</c>): a cache that keys on the URL alone must not serve one caller's address to another. The
/// response is also <c>private</c> and short-lived: a proxy that passes a forwarded header through unchecked, in front of
/// a shared cache that ignores <c>Vary</c>, would otherwise keep one caller's address for every caller for an hour.
/// </para>
/// </remarks>
internal static class A2AAgentCardResponse
{
    private const string CacheControl = "private, max-age=300";

    private static readonly string[] AddressHeaders = ["Host", "X-Forwarded-Host", "X-Forwarded-Proto"];

    /// <summary>Builds the answer to a card request.</summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="interfacePath">The path of the interface under the application's path base, such as <c>/tracon/a2a/support</c>.</param>
    /// <param name="build">Builds the card for an interface address.</param>
    /// <param name="lastModified">The <c>Last-Modified</c> value: when the card was mapped.</param>
    /// <returns>The JSON of the card.</returns>
    public static IResult Create(HttpContext httpContext, string interfacePath, Func<string, AgentCard> build, string lastModified)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(build);

        var card = build(InterfaceAddress(httpContext.Request, interfacePath));
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(card, A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(AgentCard))));

        var headers = httpContext.Response.Headers;
        headers.CacheControl = CacheControl;
        headers.ETag = "\"" + Convert.ToHexString(SHA256.HashData(bytes)) + "\"";
        headers.LastModified = lastModified;
        headers.Append(HeaderNames.Vary, AddressHeaders);

        return Results.Bytes(bytes, "application/json");
    }

    /// <summary>The <c>Last-Modified</c> value for a card mapped now.</summary>
    /// <param name="clock">The clock.</param>
    /// <returns>The date in the HTTP format.</returns>
    public static string LastModifiedNow(TimeProvider clock)
        => clock.GetUtcNow().ToString("R", CultureInfo.InvariantCulture);

    /// <summary>The address of the interface, absolute when the request names a host.</summary>
    /// <param name="request">The request.</param>
    /// <param name="interfacePath">The path of the interface under the path base.</param>
    /// <returns>
    /// <c>scheme://host/pathbase/path</c>; the path alone for a request with no host (an HTTP/1.0 request without a
    /// <c>Host</c> header), which a client resolves against the card's own URL as before.
    /// </returns>
    internal static string InterfaceAddress(HttpRequest request, string interfacePath)
    {
        var relative = request.PathBase.ToUriComponent() + interfacePath;

        if (!request.Host.HasValue)
        {
            return relative;
        }

        var absolute = $"{request.Scheme}://{request.Host.ToUriComponent()}{relative}";

        return Uri.TryCreate(absolute, UriKind.Absolute, out var uri)
               && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                   || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            ? absolute
            : relative;
    }
}
