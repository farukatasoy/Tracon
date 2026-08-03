using System.Net;
using System.Net.Sockets;

namespace AgentPrism;

/// <summary>Bir webhook hedefinin guvenli olup olmadigini bildiren sonuc.</summary>
/// <param name="IsAllowed">Hedefe istek atilabilir mi.</param>
/// <param name="Reason">Reddedilme sebebi. Izin verildiyse <see langword="null"/>.</param>
/// <param name="ResolvedAddress">
/// Cozulmus IP adresi. Baglanti <strong>bu adrese</strong> kurulur; cozumleme ile
/// baglanti arasinda adres degistirilemez (DNS yeniden baglama savunmasi).
/// </param>
public readonly record struct WebhookUrlVerdict(bool IsAllowed, string? Reason, IPAddress? ResolvedAddress);

/// <summary>
/// Webhook hedef adreslerini SSRF'e karsi denetler.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <strong>Bu fazin en buyuk guvenlik riski.</strong> Webhook adresini
/// <em>kullanici</em> verir ve sunucu o adrese istek atar. Kontrolsuz
/// birakilirsa ic agdaki servislere erisim araci olur — bulut metadata uclari
/// (<c>169.254.169.254</c>) dahil, ki bunlar cogu zaman kimlik dogrulamasiz
/// gecici kimlik bilgisi dagitir.
/// </para>
/// <para>Savunma katmanlari:</para>
/// <list type="number">
///   <item><description>Sema: yalnizca <c>https</c>; <c>http</c> yalnizca loopback ve acik izinle.</description></item>
///   <item><description>Adres: DNS cozulur, ozel ag araliklari reddedilir.</description></item>
///   <item><description>Yeniden baglama: cozulen IP'ye dogrudan baglanilir, <c>Host</c> basligi korunur.</description></item>
///   <item><description>Yonlendirme: izlenmez — yonlendirme ozel aga kacis yoludur.</description></item>
/// </list>
/// </remarks>
public static class WebhookUrlValidator
{
    /// <summary>Bir adresi semasina gore, DNS cozmeden denetler.</summary>
    /// <param name="url">Denetlenecek adres.</param>
    /// <param name="settings">Webhook ayarlari.</param>
    /// <returns>Sonuc. Adres bicimi veya semasi gecersizse reddeder.</returns>
    /// <remarks>
    /// Kaydetme aninda (HTTP ucunda) bu kullanilir: DNS cozumlemesi kaydi
    /// yavaslatir ve hedef o an erisilemez olabilir. Gercek koruma teslim
    /// aninda <see cref="ValidateResolvedAsync"/> ile uygulanir.
    /// </remarks>
    public static WebhookUrlVerdict ValidateFormat(string? url, AgentPrismWebhookOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(url))
        {
            return new WebhookUrlVerdict(false, "Adres bos olamaz.", null);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return new WebhookUrlVerdict(false, "Adres mutlak bir URI olmalidir.", null);
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return new WebhookUrlVerdict(true, null, null);
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal))
        {
            return new WebhookUrlVerdict(false, $"'{uri.Scheme}' semasi desteklenmiyor; yalnizca https kullanin.", null);
        }

        // http YALNIZCA loopback hedefleri icin ve acik izinle. Yerel
        // gelistirmede gerekir; disari acik bir adrese sifrelenmemis olay
        // gondermek olay icerigini aga acar.
        if (!settings.AllowInsecureHttp)
        {
            return new WebhookUrlVerdict(false, "http desteklenmiyor; https kullanin veya AllowInsecureHttp ayarini acin.", null);
        }

        if (!IsLoopbackHost(uri))
        {
            return new WebhookUrlVerdict(false, "http yalnizca loopback (localhost) hedefleri icin kullanilabilir.", null);
        }

        return new WebhookUrlVerdict(true, null, null);
    }

    /// <summary>Bir adresi DNS cozerek ve IP araligina bakarak denetler.</summary>
    /// <param name="url">Denetlenecek adres.</param>
    /// <param name="settings">Webhook ayarlari.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Sonuc. Izin verilirse cozulen adresi tasir.</returns>
    /// <remarks>
    /// Teslim aninda cagrilir. Donen <see cref="WebhookUrlVerdict.ResolvedAddress"/>
    /// baglanti kurulurken <strong>kullanilmalidir</strong>; adresi yeniden
    /// cozmek DNS yeniden baglama saldirisina kapi acar.
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
                return new WebhookUrlVerdict(false, $"Adres cozumlenemedi: {exception.Message}", null);
            }
        }

        if (addresses.Length == 0)
        {
            return new WebhookUrlVerdict(false, "Adres hicbir IP'ye cozumlenmedi.", null);
        }

        // 🚨 Cozulen adreslerin HERHANGI biri reddedilirse hedef reddedilir.
        // "Ilk uygun adresi sec" demek, saldirganin bir genel ve bir ozel adres
        // donduren bir ad yayimlamasina izin verirdi.
        foreach (var address in addresses)
        {
            if (!IsAllowedTarget(address, settings))
            {
                return new WebhookUrlVerdict(
                    false,
                    $"Hedef ozel bir ag adresine ({address}) cozumleniyor; AllowPrivateNetworkTargets kapali.",
                    null);
            }
        }

        return new WebhookUrlVerdict(true, null, addresses[0]);
    }

    /// <summary>Bir adrese teslim yapilip yapilamayacagini bildirir.</summary>
    /// <param name="address">Cozulmus adres.</param>
    /// <param name="settings">Webhook ayarlari.</param>
    /// <returns>Adrese baglanilabiliyorsa <see langword="true"/>.</returns>
    /// <remarks>
    /// <para>
    /// 🚨 Loopback, <see cref="AgentPrismWebhookOptions.AllowInsecureHttp"/>
    /// acikken kabul edilir. Sebep: o ayar zaten "bu bir yerel gelistirme
    /// kurulumudur" demektir ve yalnizca loopback hedefleri icin gecerlidir.
    /// Aksi halde yerel bir dinleyiciyi sinamak, <c>10/8</c> ve
    /// <c>169.254.169.254</c> dahil <strong>tum</strong> ozel agi acan
    /// <see cref="AgentPrismWebhookOptions.AllowPrivateNetworkTargets"/>
    /// ayarini gerektirirdi — gelistirme kolayligi ugruna uretim guvenligini
    /// feda etmek olurdu (K-167).
    /// </para>
    /// <para>
    /// Loopback disindaki hicbir ozel aralik bu ayarla acilmaz.
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

    /// <summary>Bir IP adresinin ozel/yerel bir aralikta olup olmadigini bildirir.</summary>
    /// <param name="address">Adres.</param>
    /// <returns>Adres ozel bir aralikta ise <see langword="true"/>.</returns>
    /// <remarks>
    /// Kapsanan araliklar: <c>127.0.0.0/8</c>, <c>10.0.0.0/8</c>,
    /// <c>172.16.0.0/12</c>, <c>192.168.0.0/16</c>, <c>169.254.0.0/16</c>
    /// (bulut metadata!), <c>100.64.0.0/10</c> (CGNAT), <c>0.0.0.0/8</c>,
    /// <c>::1</c>, <c>fc00::/7</c>, <c>fe80::/10</c> ve IPv4'e eslenmis IPv6
    /// adresleri.
    /// </remarks>
    public static bool IsPrivate(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        // 🚨 IPv4'e eslenmis IPv6 (::ffff:169.254.169.254) denetimi atlatmanin
        // klasik yoludur; once duz IPv4'e indirgenir.
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

            // fc00::/7 -- benzersiz yerel adresler.
            if ((bytes[0] & 0xFE) == 0xFC)
            {
                return true;
            }

            // :: (belirtilmemis)
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
