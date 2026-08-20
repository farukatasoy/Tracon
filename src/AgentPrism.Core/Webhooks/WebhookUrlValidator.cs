using System.Net;
using System.Net.Sockets;

namespace AgentPrism;

/// <summary>The result reporting whether a webhook target is safe.</summary>
/// <param name="IsAllowed">Whether a request can be sent to the target.</param>
/// <param name="Reason">The reason for rejection. <see langword="null"/> if allowed.</param>
/// <param name="ResolvedAddress">
/// The resolved IP address. The connection is established <strong>to this
/// address</strong>; the address cannot change between resolution and
/// connection (a DNS rebinding defense).
/// </param>
public readonly record struct WebhookUrlVerdict(bool IsAllowed, string? Reason, IPAddress? ResolvedAddress);

/// <summary>
/// Validates webhook target addresses against SSRF.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This phase's biggest security risk.</strong> The webhook
/// address is given by the <em>user</em>, and the server sends a request to
/// that address. If left uncontrolled, it becomes a means of reaching
/// internal-network services — including cloud metadata endpoints
/// (<c>169.254.169.254</c>), which often hand out unauthenticated temporary
/// credentials.
/// </para>
/// <para>Defense layers:</para>
/// <list type="number">
/// <item>
/// <description>Scheme: <c>https</c> only; <c>http</c> only for loopback and with explicit permission.</description>
/// </item>
///   <item><description>Address: DNS is resolved, private network ranges are rejected.</description></item>
/// <item>
/// <description>Rebinding: connects directly to the resolved IP, with the <c>Host</c> header preserved.</description>
/// </item>
///   <item><description>Redirects: not followed — a redirect is an escape route into a private network.</description></item>
/// </list>
/// </remarks>
public static class WebhookUrlValidator
{
    /// <summary>Validates an address by its scheme, without resolving DNS.</summary>
    /// <param name="url">The address to validate.</param>
    /// <param name="settings">The webhook settings.</param>
    /// <returns>The result. Rejects if the address format or scheme is invalid.</returns>
    /// <remarks>
    /// Used at save time (in the HTTP endpoint): DNS resolution would slow
    /// down the save, and the target may be unreachable at that moment. Real
    /// protection is applied at delivery time, with <see cref="ValidateResolvedAsync"/>.
    /// </remarks>
    public static WebhookUrlVerdict ValidateFormat(string? url, AgentPrismWebhookOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(url))
        {
            return new WebhookUrlVerdict(false, "Address cannot be empty.", null);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return new WebhookUrlVerdict(false, "Address must be an absolute URI.", null);
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return new WebhookUrlVerdict(true, null, null);
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal))
        {
            return new WebhookUrlVerdict(false, $"Scheme '{uri.Scheme}' is not supported; use https only.", null);
        }

        // http is ONLY for loopback targets and requires explicit permission.
        // Needed in local development; sending an unencrypted event to a
        // publicly reachable address would expose the event's content to the network.
        if (!settings.AllowInsecureHttp)
        {
            return new WebhookUrlVerdict(false, "http is not supported; use https or enable the AllowInsecureHttp setting.", null);
        }

        if (!IsLoopbackHost(uri))
        {
            return new WebhookUrlVerdict(false, "http can only be used for loopback (localhost) targets.", null);
        }

        return new WebhookUrlVerdict(true, null, null);
    }

    /// <summary>Validates an address by resolving DNS and checking the IP range.</summary>
    /// <param name="url">The address to validate.</param>
    /// <param name="settings">The webhook settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result. Carries the resolved address if allowed.</returns>
    /// <remarks>
    /// Called at delivery time. The returned <see cref="WebhookUrlVerdict.ResolvedAddress"/>
    /// <strong>must be used</strong> when establishing the connection;
    /// resolving the address again opens the door to a DNS rebinding attack.
    /// </remarks>
    public static async ValueTask<WebhookUrlVerdict> ValidateResolvedAsync(
        string? url,
        AgentPrismWebhookOptions settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var formatVerdict = ValidateFormat(url, settings);

        if (!formatVerdict.IsAllowed)
        {
            return formatVerdict;
        }

        var uri = new Uri(url!, UriKind.Absolute);

        IPAddress[] addresses;

        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException exception)
            {
                return new WebhookUrlVerdict(false, $"Address could not be resolved: {exception.Message}", null);
            }
        }

        if (addresses.Length == 0)
        {
            return new WebhookUrlVerdict(false, "Address did not resolve to any IP.", null);
        }

        // 🚨 If ANY of the resolved addresses is rejected, the target is rejected.
        // "Pick the first suitable address" would let an attacker publish a
        // name that resolves to one public and one private address.
        foreach (var address in addresses)
        {
            if (!IsAllowedTarget(address, settings))
            {
                return new WebhookUrlVerdict(
                    false,
                    $"The target resolves to a private network address ({address}); AllowPrivateNetworkTargets is disabled.",
                    null);
            }
        }

        return new WebhookUrlVerdict(true, null, addresses[0]);
    }

    /// <summary>Reports whether delivery to an address is allowed.</summary>
    /// <param name="address">The resolved address.</param>
    /// <param name="settings">The webhook settings.</param>
    /// <returns><see langword="true"/> if a connection to the address can be made.</returns>
    /// <remarks>
    /// <para>
    /// Loopback is accepted while <see cref="AgentPrismWebhookOptions.AllowInsecureHttp"/>
    /// is enabled. That setting already means "this is a local
    /// development setup" and applies only to loopback targets. Otherwise,
    /// testing a local listener would require
    /// <see cref="AgentPrismWebhookOptions.AllowPrivateNetworkTargets"/>,
    /// which opens the <strong>entire</strong> private network, including
    /// <c>10/8</c> and <c>169.254.169.254</c> — this would trade production
    /// security for development convenience.
    /// </para>
    /// <para>
    /// No private range other than loopback is opened by this setting.
    /// </para>
    /// </remarks>
    public static bool IsAllowedTarget(IPAddress address, AgentPrismWebhookOptions settings)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.AllowPrivateNetworkTargets)
        {
            return true;
        }

        if (IPAddress.IsLoopback(address))
        {
            return settings.AllowInsecureHttp;
        }

        return !IsPrivate(address);
    }

    /// <summary>Reports whether an IP address falls within a private/local range.</summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> if the address falls within a private range.</returns>
    /// <remarks>
    /// Ranges covered: <c>127.0.0.0/8</c>, <c>10.0.0.0/8</c>,
    /// <c>172.16.0.0/12</c>, <c>192.168.0.0/16</c>, <c>169.254.0.0/16</c>
    /// (cloud metadata!), <c>100.64.0.0/10</c> (CGNAT), <c>0.0.0.0/8</c>,
    /// <c>::1</c>, <c>fc00::/7</c>, <c>fe80::/10</c>, and IPv4-mapped IPv6 addresses.
    /// </remarks>
    public static bool IsPrivate(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        // 🚨 IPv4-mapped IPv6 (::ffff:169.254.169.254) is a classic way to
        // bypass the check; it is reduced to plain IPv4 first.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var octets = address.GetAddressBytes();

            return octets[0] switch
            {
                0 => true,                                        // 0.0.0.0/8
                10 => true,                                       // 10/8
                127 => true,                                      // 127/8
                169 when octets[1] == 254 => true,                // 169.254/16 -- metadata
                172 when octets[1] >= 16 && octets[1] <= 31 => true, // 172.16/12
                192 when octets[1] == 168 => true,                // 192.168/16
                100 when octets[1] >= 64 && octets[1] <= 127 => true, // 100.64/10 CGNAT
                >= 224 => true,                                   // multicast + rezerve
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

    private static bool IsLoopbackHost(Uri uri)
        => uri.IsLoopback
           || (IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address));
}
