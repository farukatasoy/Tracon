using System.Net;

namespace AgentPrism;

/// <summary>
/// Determines whether a request comes from the same machine.
/// </summary>
/// <remarks>
/// This is <em>not</em> authentication; it is a default gate against accidental exposure.
/// Real protection comes from a bearer token or from an authorization policy.
/// </remarks>
internal static class LoopbackGuard
{
    /// <summary>Determines whether the remote address belongs to the local machine.</summary>
    /// <param name="remoteAddress">
    /// The remote address of the connection. It is <see langword="null"/> when the request
    /// does not come from a network socket (an in-process test host, a Unix socket).
    /// </param>
    /// <returns><see langword="true"/> when the address is local.</returns>
    /// <remarks>
    /// <para>
    /// When <paramref name="remoteAddress"/> is <see langword="null"/>, the request counts as
    /// local: it did not arrive over a TCP connection, so it is not remote.
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
}
