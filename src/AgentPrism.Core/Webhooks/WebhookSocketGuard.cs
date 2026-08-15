using System.Net;
using System.Net.Sockets;

namespace AgentPrism;

/// <summary>
/// The connection callback for the webhook <see cref="HttpClient"/>: resolves
/// the address to connect to, validates it against the SSRF rules, and opens
/// a socket only to an allowed address.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Having the check happen <strong>here</strong> is deliberate. Validating
/// first and then calling <c>HttpClient.SendAsync(url)</c> leaves a TOCTOU
/// gap: <c>HttpClient</c> resolves the name again, and an attacker could
/// change the answer between the two resolutions (DNS rebinding). The
/// address validated in the connection callback <em>is the very address the
/// socket connects to</em>; there is no further resolution in between.
/// </para>
/// <para>
/// The <c>Host</c> header is set from, and preserved as, the original name by
/// <c>HttpClient</c>; TLS validation is also performed against the original name.
/// </para>
/// </remarks>
internal static class WebhookSocketGuard
{
    /// <summary>Produces a validating connection callback.</summary>
    /// <param name="optionsAccessor">The accessor that returns the current webhook settings.</param>
    /// <returns>A delegate suitable for <see cref="SocketsHttpHandler.ConnectCallback"/>.</returns>
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> Create(
        Func<AgentPrismWebhookOptions> optionsAccessor)
    {
        ArgumentNullException.ThrowIfNull(optionsAccessor);

        return async (context, cancellationToken) =>
        {
            var options = optionsAccessor();
            var host = context.DnsEndPoint.Host;
            var port = context.DnsEndPoint.Port;

            IPAddress[] addresses = IPAddress.TryParse(host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

            if (addresses.Length == 0)
            {
                throw new AgentPrismException($"The webhook target could not be resolved: {host}.");
            }

            // 🚨 If ANY of the addresses is rejected, the connection is not established.
            // "Pick the valid one" would let a name that resolves to one
            // public and one private address bypass the check.
            foreach (var address in addresses)
            {
                if (!WebhookUrlValidator.IsAllowedTarget(address, options))
                {
                    throw new AgentPrismException(
                        $"The webhook target resolves to a private network address ({address}); the connection was rejected.");
                }
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

            try
            {
                // Connects to the validated addresses; the name is not resolved again.
                await socket.ConnectAsync(addresses, port, cancellationToken).ConfigureAwait(false);

                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
    }
}
