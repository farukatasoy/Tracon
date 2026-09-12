using System.Net;

namespace Tracon;

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
/// <strong>The webhook subsystem's biggest security risk.</strong> The webhook
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
    public static WebhookUrlVerdict ValidateFormat(string? url, TraconWebhookOptions settings)
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
    /// <param name="egress">The shared egress settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result. Carries the resolved address if allowed.</returns>
    /// <remarks>
    /// Called at delivery time. The returned <see cref="WebhookUrlVerdict.ResolvedAddress"/>
    /// <strong>must be used</strong> when establishing the connection;
    /// resolving the address again opens the door to a DNS rebinding attack.
    /// </remarks>
    public static async ValueTask<WebhookUrlVerdict> ValidateResolvedAsync(
        string? url,
        TraconWebhookOptions settings,
        TraconEgressOptions egress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(egress);

        var formatVerdict = ValidateFormat(url, settings);

        if (!formatVerdict.IsAllowed)
        {
            return formatVerdict;
        }

        var uri = new Uri(url!, UriKind.Absolute);

        var verdict = await EgressAddressValidator
            .ResolveAndValidateAsync(uri.Host, ToPolicy(settings, egress), cancellationToken)
            .ConfigureAwait(false);

        return verdict.IsAllowed
            ? new WebhookUrlVerdict(true, null, verdict.ResolvedAddresses![0])
            : new WebhookUrlVerdict(false, verdict.Reason, null);
    }

    /// <summary>Reports whether delivery to an address is allowed.</summary>
    /// <param name="address">The resolved address.</param>
    /// <param name="settings">The webhook settings.</param>
    /// <returns><see langword="true"/> if a connection to the address can be made.</returns>
    /// <remarks>
    /// <para>
    /// Loopback is accepted while <see cref="TraconWebhookOptions.AllowInsecureHttp"/>
    /// is enabled. That setting already means "this is a local
    /// development setup" and applies only to loopback targets. Otherwise,
    /// testing a local listener would require
    /// <see cref="TraconWebhookOptions.AllowPrivateNetworkTargets"/>,
    /// which opens the <strong>entire</strong> private network, including
    /// <c>10/8</c> and <c>169.254.169.254</c> — this would trade production
    /// security for development convenience.
    /// </para>
    /// <para>
    /// No private range other than loopback is opened by that setting.
    /// </para>
    /// </remarks>
    public static bool IsAllowedTarget(IPAddress address, TraconWebhookOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return EgressAddressValidator.IsAllowedTarget(address, ToPolicy(settings, egress: null));
    }

    /// <summary>Reports whether an IP address falls within a private/local range.</summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> if the address falls within a private range.</returns>
    /// <remarks>
    /// The rules live in <see cref="EgressAddressValidator.IsPrivate"/>, which
    /// all three outbound surfaces share. This member forwards to it and holds
    /// no copy of its own.
    /// </remarks>
    public static bool IsPrivate(IPAddress address) => EgressAddressValidator.IsPrivate(address);

    /// <summary>Converts webhook settings into the shared egress address policy.</summary>
    /// <param name="settings">The webhook settings.</param>
    /// <param name="egress">
    /// The shared egress settings, or <see langword="null"/> to consider only
    /// the webhook settings.
    /// </param>
    /// <returns>The policy applied to webhook targets.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Both flags matter. <see cref="TraconWebhookOptions.AllowPrivateNetworkTargets"/>
    /// predates the shared <see cref="TraconEgressOptions"/> and stays
    /// honoured, so a setup that already opened the private network for
    /// webhooks keeps working. The shared option is read where the policy is
    /// built, in <c>WebhookHttpClient</c> and <c>WebhookDeliveryJobHandler</c>;
    /// either one being enabled is enough.
    /// </remarks>
    public static EgressAddressPolicy ToPolicy(TraconWebhookOptions settings, TraconEgressOptions? egress)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new EgressAddressPolicy(
            settings.AllowPrivateNetworkTargets || (egress?.AllowPrivateNetworkTargets ?? false),
            AllowLoopback: settings.AllowInsecureHttp);
    }

    private static bool IsLoopbackHost(Uri uri)
        => uri.IsLoopback
           || (IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address));
}
