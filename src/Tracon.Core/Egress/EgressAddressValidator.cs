using System.Net;
using System.Net.Sockets;

namespace Tracon;

/// <summary>The result of validating an outbound target address.</summary>
/// <param name="IsAllowed">Whether a connection to the target may be opened.</param>
/// <param name="Reason">The reason for rejection. <see langword="null"/> if allowed.</param>
/// <param name="ResolvedAddresses">
/// Every address the host resolved to. The connection is established
/// <strong>to these addresses</strong>; resolving the name again would reopen
/// the DNS rebinding window.
/// </param>
public readonly record struct EgressAddressVerdict(
    bool IsAllowed,
    string? Reason,
    IPAddress[]? ResolvedAddresses);

/// <summary>
/// The single place that decides whether an outbound target address is safe.
/// </summary>
/// <remarks>
/// <para>
/// Tracon reaches the network from three surfaces — webhook delivery, MCP
/// server connections and model provider calls. Each one accepts an address
/// that ultimately came from a <em>user</em>, so each one is an SSRF surface:
/// left uncontrolled, they become a means of reaching internal-network
/// services, including cloud metadata endpoints (<c>169.254.169.254</c>),
/// which often hand out unauthenticated temporary credentials.
/// </para>
/// <para>
/// The rules live here, once. Three copies of the same resolve-and-check loop
/// end the same way: one gets fixed and two go stale.
/// </para>
/// </remarks>
public static class EgressAddressValidator
{
    /// <summary>Reports whether an address may be connected to under a policy.</summary>
    /// <param name="address">The resolved address.</param>
    /// <param name="policy">The rules of the calling surface.</param>
    /// <returns><see langword="true"/> if a connection to the address may be opened.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is <see langword="null"/>.</exception>
    public static bool IsAllowedTarget(IPAddress address, EgressAddressPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (policy.AllowPrivateNetworkTargets)
        {
            return true;
        }

        if (IPAddress.IsLoopback(address))
        {
            return policy.AllowLoopback;
        }

        return !IsPrivate(address);
    }

    /// <summary>Reports whether an IP address falls within a private or local range.</summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> if the address falls within a private range.</returns>
    /// <remarks>
    /// <para>
    /// IPv4 ranges covered: <c>0.0.0.0/8</c>, <c>10.0.0.0/8</c>,
    /// <c>127.0.0.0/8</c>, <c>169.254.0.0/16</c> (cloud metadata!),
    /// <c>172.16.0.0/12</c>, <c>192.168.0.0/16</c>, <c>100.64.0.0/10</c>
    /// (CGNAT), and everything from <c>224.0.0.0</c> up (multicast and reserved).
    /// </para>
    /// <para>
    /// IPv6 ranges covered: <c>::1</c>, <c>::</c>, <c>fc00::/7</c>,
    /// <c>fe80::/10</c>, site-local, and multicast.
    /// </para>
    /// <para>
    /// An IPv6 address that <em>embeds</em> an IPv4 address is reduced to
    /// that IPv4 address first and then judged by the IPv4 rules. Skipping
    /// this is a classic bypass: <c>::ffff:169.254.169.254</c>,
    /// <c>::169.254.169.254</c>, <c>64:ff9b::a9fe:a9fe</c> and
    /// <c>2002:a9fe:a9fe::</c> all reach the metadata endpoint through a host
    /// that has the matching translation or relay configured. See
    /// <see cref="TryGetEmbeddedIPv4"/> for the forms handled.
    /// </para>
    /// </remarks>
    public static bool IsPrivate(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // 🚨 The local-use NAT64 prefix (RFC 8215) is rejected whole rather
            // than decoded. Its embedded-IPv4 layout depends on the prefix
            // length the operator picked, and no legitimate public destination
            // lives under a prefix that is reserved for local translation.
            if (IsLocalUseNat64(address))
            {
                return true;
            }

            if (TryGetEmbeddedIPv4(address, out var embedded))
            {
                address = embedded;
            }
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var octets = address.GetAddressBytes();

            return octets[0] switch
            {
                0 => true,                                           // 0.0.0.0/8
                10 => true,                                          // 10/8
                127 => true,                                         // 127/8
                169 when octets[1] == 254 => true,                   // 169.254/16 -- metadata
                172 when octets[1] >= 16 && octets[1] <= 31 => true,  // 172.16/12
                192 when octets[1] == 168 => true,                   // 192.168/16
                100 when octets[1] >= 64 && octets[1] <= 127 => true, // 100.64/10 CGNAT
                >= 224 => true,                                      // multicast + reserved
                _ => false,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            {
                return true;
            }

            var bytes = address.GetAddressBytes();

            // fc00::/7 -- unique local addresses.
            if ((bytes[0] & 0xFE) == 0xFC)
            {
                return true;
            }

            // :: (unspecified)
            if (address.Equals(IPAddress.IPv6Any))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Extracts the IPv4 address an IPv6 address embeds, if it embeds one.</summary>
    /// <param name="address">The IPv6 address.</param>
    /// <param name="embedded">The embedded IPv4 address, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the address carries an embedded IPv4 address.</returns>
    /// <remarks>
    /// Forms handled, each of which routes to the embedded IPv4 address on a
    /// host with the matching translation configured:
    /// <list type="bullet">
    ///   <item><description>IPv4-mapped, <c>::ffff:0:0/96</c> — <c>::ffff:1.2.3.4</c>.</description></item>
    ///   <item><description>IPv4-translated, <c>::ffff:0:0:0/96</c> (RFC 2765).</description></item>
    ///   <item><description>IPv4-compatible, <c>::/96</c> (RFC 4291, deprecated) — <c>::1.2.3.4</c>.</description></item>
    ///   <item><description>NAT64 well-known prefix, <c>64:ff9b::/96</c> (RFC 6052).</description></item>
    ///   <item><description>6to4, <c>2002::/16</c> (RFC 3056) — the IPv4 address sits in the second and third groups.</description></item>
    /// </list>
    /// Teredo (<c>2001::/32</c>) is deliberately not decoded: its client
    /// address is obfuscated and its server address is not the destination.
    /// </remarks>
    public static bool TryGetEmbeddedIPv4(IPAddress address, out IPAddress embedded)
    {
        ArgumentNullException.ThrowIfNull(address);

        embedded = IPAddress.None;

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            embedded = address.MapToIPv4();

            return true;
        }

        var bytes = address.GetAddressBytes();

        // 6to4: 2002:AABB:CCDD::/48 carries 0xAA.0xBB.0xCC.0xDD.
        if (bytes[0] == 0x20 && bytes[1] == 0x02)
        {
            embedded = new IPAddress(bytes.AsSpan(2, 4).ToArray());

            return true;
        }

        // NAT64 well-known prefix 64:ff9b::/96 -- the IPv4 address is the last four bytes.
        if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B
            && IsZero(bytes.AsSpan(4, 8)))
        {
            embedded = new IPAddress(bytes.AsSpan(12, 4).ToArray());

            return true;
        }

        // IPv4-translated ::ffff:0:0:0/96 -- bytes 8 and 9 are 0xFF, the rest of the prefix is zero.
        if (IsZero(bytes.AsSpan(0, 8)) && bytes[8] == 0xFF && bytes[9] == 0xFF
            && bytes[10] == 0x00 && bytes[11] == 0x00)
        {
            embedded = new IPAddress(bytes.AsSpan(12, 4).ToArray());

            return true;
        }

        // IPv4-compatible ::a.b.c.d -- the whole 96-bit prefix is zero.
        // :: and ::1 are excluded; both are already caught as private on their own.
        if (IsZero(bytes.AsSpan(0, 12)) && !IsZero(bytes.AsSpan(12, 4)) && bytes[15] != 0x01)
        {
            embedded = new IPAddress(bytes.AsSpan(12, 4).ToArray());

            return true;
        }

        return false;
    }

    /// <summary>Validates a target whose host is written as an IP literal, without resolving DNS.</summary>
    /// <param name="target">The target address.</param>
    /// <param name="policy">The rules of the calling surface.</param>
    /// <returns>The rejection reason, or <see langword="null"/> when the target is acceptable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Used at save time, in the HTTP endpoints. DNS is deliberately
    /// <strong>not</strong> resolved there: it would slow the save down, and a
    /// target that is unreachable at that moment — or a name that does not
    /// resolve yet — is not by itself an error. A host name that resolves to a
    /// private address still gets rejected, but at connection time, by
    /// <see cref="EgressSocketGuard"/>. That check is the one that cannot be
    /// evaded; this one only turns the obvious mistake into an immediate
    /// <c>400</c> instead of a silent failure hours later.
    /// </remarks>
    public static string? ValidateLiteral(Uri target, EgressAddressPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!IPAddress.TryParse(target.Host, out var literal))
        {
            return null;
        }

        return IsAllowedTarget(literal, policy)
            ? null
            : Describe(literal);
    }

    /// <summary>Resolves a host name and validates every address it resolves to.</summary>
    /// <param name="host">The host name or IP literal.</param>
    /// <param name="policy">The rules of the calling surface.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The verdict, carrying every resolved address when allowed.</returns>
    /// <remarks>
    /// If <strong>any</strong> resolved address is rejected, the target is
    /// rejected. Picking the first suitable address instead would let an
    /// attacker publish a name that resolves to one public and one private
    /// address, and reach the private one on the next attempt.
    /// </remarks>
    public static async ValueTask<EgressAddressVerdict> ResolveAndValidateAsync(
        string host,
        EgressAddressPolicy policy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return new EgressAddressVerdict(false, "The target host is empty.", null);
        }

        IPAddress[] addresses;

        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is SocketException or ArgumentException)
            {
                // ArgumentException covers a host that is malformed or longer
                // than 255 characters: Dns rejects it before it reaches the
                // network. Letting it escape would turn a rejected target into
                // an unhandled exception inside a connection callback.
                //
                // 🚨 The OS-level SocketException/ArgumentException message is foreign
                // text (Phase 119, BL-027/BL-037): this reason reaches webhook_deliveries.error
                // (via WebhookDeliveryJobHandler) as well as admin-facing validation responses,
                // and this shared validator has no logger to pair a correlation id with. Only
                // the exception's type name is kept.
                return new EgressAddressVerdict(false, $"The target could not be resolved ({exception.GetType().Name}).", null);
            }
        }

        return ValidateAddresses(addresses, policy);
    }

    /// <summary>Validates every address a host resolved to.</summary>
    /// <param name="addresses">The resolved addresses.</param>
    /// <param name="policy">The rules of the calling surface.</param>
    /// <returns>The verdict, carrying the addresses unchanged when allowed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="addresses"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// If <strong>any</strong> address is rejected, the whole target is
    /// rejected. Picking the first suitable address instead would let an
    /// attacker publish a name that resolves to one public and one private
    /// address, and reach the private one on a later attempt.
    /// </remarks>
    public static EgressAddressVerdict ValidateAddresses(IPAddress[] addresses, EgressAddressPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        if (addresses.Length == 0)
        {
            return new EgressAddressVerdict(false, "The target did not resolve to any IP address.", null);
        }

        foreach (var address in addresses)
        {
            if (!IsAllowedTarget(address, policy))
            {
                return new EgressAddressVerdict(false, Describe(address), null);
            }
        }

        return new EgressAddressVerdict(true, null, addresses);
    }

    private static string Describe(IPAddress address)
        => $"The target resolves to a private network address ({address}); set " +
           $"'{TraconEgressOptions.SectionName}:{nameof(TraconEgressOptions.AllowPrivateNetworkTargets)}' " +
           "to true to allow it.";

    private static bool IsLocalUseNat64(IPAddress address)
    {
        var bytes = address.GetAddressBytes();

        // 64:ff9b:1::/48 -- RFC 8215.
        return bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B
            && bytes[4] == 0x00 && bytes[5] == 0x01;
    }

    private static bool IsZero(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value != 0)
            {
                return false;
            }
        }

        return true;
    }
}
