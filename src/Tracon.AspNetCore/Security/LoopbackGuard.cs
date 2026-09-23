using System.Net;
using Microsoft.AspNetCore.Http;

namespace Tracon;

/// <summary>
/// Determines whether a request comes from the same machine.
/// </summary>
/// <remarks>
/// This is <em>not</em> authentication; it is a default gate against accidental exposure.
/// Real protection comes from a bearer token or from an authorization policy.
/// </remarks>
internal static class LoopbackGuard
{
    /// <summary>
    /// The headers a reverse proxy adds to say it forwarded someone else's request.
    /// </summary>
    /// <remarks>
    /// The <c>ForwardedHeaders</c> middleware consumes them: once it has moved the
    /// client address into the connection, the header is gone and the connection
    /// address is the truth. A header that is still present means nothing
    /// consumed it, so the connection address is the proxy's, not the client's.
    /// </remarks>
    private static readonly string[] ForwardingHeaders = ["X-Forwarded-For", "Forwarded", "X-Real-IP"];

    /// <summary>Determines whether the remote address belongs to the local machine.</summary>
    /// <param name="remoteAddress">
    /// The remote address of the connection. It is <see langword="null"/> when the request
    /// does not come from a network socket (an in-process test host, a Unix socket).
    /// </param>
    /// <returns><see langword="true"/> when the address is local.</returns>
    /// <remarks>
    /// <para>
    /// When <paramref name="remoteAddress"/> is <see langword="null"/>, the request counts as
    /// local: it did not arrive over a TCP connection, so it is not remote. A reverse proxy
    /// that listens on a Unix socket is caught by <see cref="IsLocalRequest"/>, which also
    /// looks for the headers the proxy adds.
    /// </para>
    /// <para>
    /// IPv4 addresses mapped to IPv6 (<c>::ffff:127.0.0.1</c>) are converted to IPv4 first;
    /// <see cref="IPAddress.IsLoopback"/> does not recognize the mapped form.
    /// </para>
    /// </remarks>
    public static bool IsLocal(IPAddress? remoteAddress)
    {
        if (remoteAddress is null)
        {
            return true;
        }

        if (remoteAddress.IsIPv4MappedToIPv6)
        {
            remoteAddress = remoteAddress.MapToIPv4();
        }

        return IPAddress.IsLoopback(remoteAddress);
    }

    /// <summary>
    /// Determines whether a request comes from the same machine: a local
    /// connection that no reverse proxy forwarded.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <returns><see langword="true"/> when the request is local.</returns>
    /// <remarks>
    /// A reverse proxy on the same machine connects over loopback or a Unix socket,
    /// so every request it forwards has a local connection address. Its forwarding
    /// header is what gives it away. A local caller that adds such a header itself
    /// only lowers its own access.
    /// </remarks>
    public static bool IsLocalRequest(HttpContext httpContext)
        => IsLocal(httpContext.Connection.RemoteIpAddress) && !HasForwardingHeader(httpContext.Request);

    /// <summary>Determines whether a reverse proxy marked the request as forwarded.</summary>
    /// <param name="request">The current request.</param>
    /// <returns><see langword="true"/> when a forwarding header is present.</returns>
    public static bool HasForwardingHeader(HttpRequest request)
    {
        foreach (var header in ForwardingHeaders)
        {
            if (request.Headers.ContainsKey(header))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether a host name can only name this machine.</summary>
    /// <param name="host">A host without its port, as <see cref="HostString.Host"/> returns it.</param>
    /// <returns>
    /// <see langword="true"/> for <c>localhost</c>, a <c>*.localhost</c> name, and a loopback
    /// IP literal (<c>127.0.0.0/8</c>, <c>[::1]</c>).
    /// </returns>
    /// <remarks>
    /// A browser resolves <c>*.localhost</c> to loopback itself and never asks DNS,
    /// so no rebinding attack can use such a name. Any other name can resolve to
    /// <c>127.0.0.1</c> while still being the attacker's name.
    /// </remarks>
    public static bool IsLoopbackHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var literal = host.Length > 1 && host[0] == '[' && host[^1] == ']' ? host[1..^1] : host;

        return IPAddress.TryParse(literal, out var address) && IsLocal(address);
    }

    /// <summary>Determines whether a browser origin names this machine.</summary>
    /// <param name="origin">The <c>Origin</c> header value.</param>
    /// <returns>
    /// <see langword="true"/> when the origin is an absolute http or https address
    /// whose host satisfies <see cref="IsLoopbackHost"/>. The opaque origin
    /// <c>null</c> (a sandboxed frame, a file) is not.
    /// </returns>
    public static bool IsLoopbackOrigin(string origin)
        => Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            && IsLoopbackHost(uri.Host);
}
