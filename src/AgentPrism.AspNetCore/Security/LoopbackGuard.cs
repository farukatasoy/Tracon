using System.Net;

namespace AgentPrism;

/// <summary>
/// Bir istegin ayni makineden gelip gelmedigine karar verir.
/// </summary>
/// <remarks>
/// Bu, kimlik dogrulama <em>degildir</em>; kaza ile disariya acilmaya karsi bir
/// varsayilan kapidir. Gercek koruma bearer token veya authorization policy ile
/// saglanir.
/// </remarks>
internal static class LoopbackGuard
{
    /// <summary>Uzak adresin yerel makineye ait olup olmadigini soyler.</summary>
    /// <param name="remoteAddress">
    /// Baglantinin uzak adresi. Istek bir ag soketinden gelmediyse
    /// (surec ici test barindiricisi, Unix soketi) <see langword="null"/> olur.
    /// </param>
    /// <returns>Adres yerelse <see langword="true"/>.</returns>
    /// <remarks>
    /// <para>
    /// <paramref name="remoteAddress"/> <see langword="null"/> ise istek yerel kabul
    /// edilir: bir TCP baglantisi uzerinden gelmemistir, dolayisiyla uzak degildir.
    /// </para>
    /// <para>
    /// IPv6'ya eslenmis IPv4 adresleri (<c>::ffff:127.0.0.1</c>) once IPv4'e
    /// cevrilir; <see cref="IPAddress.IsLoopback"/> eslenmis bicimi tanimaz.
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
