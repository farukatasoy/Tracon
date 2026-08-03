using System.Net;
using System.Net.Sockets;

namespace AgentPrism;

/// <summary>
/// Webhook <see cref="HttpClient"/>'inin baglanti geri cagrisi: baglanilacak
/// adresi cozer, SSRF kurallarina gore denetler ve yalnizca gecerli bir adrese
/// soket acar.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Denetimin <strong>burada</strong> olmasi kasitlidir. Once dogrulayip
/// sonra <c>HttpClient.SendAsync(url)</c> cagirmak bir TOCTOU acigi birakir:
/// <c>HttpClient</c> adi yeniden cozer ve saldirgan iki cozumleme arasinda
/// yaniti degistirebilir (DNS yeniden baglama). Baglanti geri cagrisinda
/// dogrulanan adres, <em>soketin baglandigi adresin ta kendisidir</em>; arada
/// bir cozumleme daha yoktur.
/// </para>
/// <para>
/// <c>Host</c> basligi <c>HttpClient</c> tarafindan ozgun adtan kurulur ve
/// korunur; TLS dogrulamasi da ozgun ada gore yapilir.
/// </para>
/// </remarks>
internal static class WebhookSocketGuard
{
    /// <summary>Denetimli bir baglanti geri cagrisi uretir.</summary>
    /// <param name="optionsAccessor">Gecerli webhook ayarlarini dondiren erisimci.</param>
    /// <returns><see cref="SocketsHttpHandler.ConnectCallback"/> icin uygun temsilci.</returns>
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
                throw new AgentPrismException($"Webhook hedefi cozumlenemedi: {host}.");
            }

            // 🚨 Adreslerin HERHANGI biri reddedilirse baglanti kurulmaz.
            // "Gecerli olani sec" demek, bir genel ve bir ozel adres donduren
            // bir adin denetimi atlatmasina izin verirdi.
            foreach (var address in addresses)
            {
                if (!WebhookUrlValidator.IsAllowedTarget(address, options))
                {
                    throw new AgentPrismException(
                        $"Webhook hedefi ozel bir ag adresine ({address}) cozumleniyor; baglanti reddedildi.");
                }
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

            try
            {
                // Dogrulanan adreslere baglanilir; ad yeniden cozulmez.
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
