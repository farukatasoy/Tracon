using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AgentPrism;

/// <summary>Webhook isteklerinin HMAC-SHA256 imzasini uretir ve dogrular.</summary>
/// <remarks>
/// <para>
/// Imza su dizeden hesaplanir: <c>{zamanDamgasi}.{govde}</c>. Zaman damgasi
/// imzaya <strong>dahildir</strong>; yoksa yakalanan bir istek sonsuza kadar
/// yeniden oynatilabilirdi (K-163).
/// </para>
/// <para>
/// Alici, imzayi dogrularken zaman damgasinin kendi toleransi icinde oldugunu
/// da denetlemelidir. AgentPrism bunu zorlayamaz; README'de yazar.
/// </para>
/// </remarks>
public static class WebhookSigner
{
    /// <summary>Olay adini tasiyan baslik.</summary>
    public const string EventHeader = "X-AgentPrism-Event";

    /// <summary>Teslim kimligini tasiyan baslik. Yinelenen teslimi ayirt etmek icin kullanilir.</summary>
    public const string DeliveryHeader = "X-AgentPrism-Delivery";

    /// <summary>Unix saniye cinsinden zaman damgasini tasiyan baslik.</summary>
    public const string TimestampHeader = "X-AgentPrism-Timestamp";

    /// <summary>Imzayi tasiyan baslik.</summary>
    public const string SignatureHeader = "X-AgentPrism-Signature";

    /// <summary>Bir govde ve zaman damgasi icin imza uretir.</summary>
    /// <param name="body">Gonderilecek JSON govde.</param>
    /// <param name="timestamp">Istegin zaman damgasi.</param>
    /// <param name="secret">Imzalama sirri.</param>
    /// <returns><c>sha256=&lt;onaltilik&gt;</c> bicimindeki imza.</returns>
    /// <exception cref="ArgumentException"><paramref name="secret"/> bos ise.</exception>
    public static string Sign(string body, DateTimeOffset timestamp, string secret)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var unixSeconds = timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signedPayload = $"{unixSeconds}.{body}";

        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(signedPayload));

        // Convert.ToHexStringLower .NET 9'da geldi; bu paket net8.0'i da hedefler.
        return $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    /// <summary>Bir imzayi dogrular.</summary>
    /// <param name="body">Alinan govde.</param>
    /// <param name="timestamp">Istekteki zaman damgasi.</param>
    /// <param name="secret">Imzalama sirri.</param>
    /// <param name="signature">Istekteki imza.</param>
    /// <returns>Imza dogruysa <see langword="true"/>.</returns>
    /// <remarks>
    /// Karsilastirma <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>
    /// ile sabit zamanlidir: siradan bir dize karsilastirmasi, ilk farkli
    /// bayta kadar gecen sureden imzayi bayt bayt tahmin etmeye izin verirdi.
    /// </remarks>
    public static bool Verify(string body, DateTimeOffset timestamp, string secret, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var expected = Sign(body, timestamp, secret);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }
}
