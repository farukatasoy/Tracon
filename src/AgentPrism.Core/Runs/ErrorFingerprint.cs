using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentPrism;

/// <summary>
/// Bir hata mesajini kumeleme parmak izine cevirir.
/// </summary>
/// <remarks>
/// <para>
/// Ham mesaj kimlik, sayi ve zaman damgasi gibi degisken parcalar tasir; her
/// satiri ayri bir kume yapardi. Normallestirme sirasi (kimlik → sayi → zaman
/// damgasi) bilinclidir: zaman damgasi, sayi degistirmesinden SONRA sayilarin
/// yerini alan <c>{n}</c> belirteclerinin ISO 8601 seklindeki dizisini yakalar
/// — ayri bir tarih dogrulama deseni yazmayi gerektirmez.
/// </para>
/// <para>
/// Tirnak ici metin BILEREK silinmez (Acik Soru 3 → C): bir tool adi ayirt
/// edicidir, silinmesi iki farkli tool hatasini tek kumede birlestirirdi.
/// Kimlik/sayi/tarih temizligi zaten mesajlarin cogunluk gurultusunu kaldirir.
/// </para>
/// </remarks>
internal static partial class ErrorFingerprint
{
    /// <summary>
    /// Hashlemeden once normallestirilmis metnin en fazla uzunlugu. Cok uzun
    /// mesajlarin (yigin izi gibi) tamami hashlenmez; kumeleme mesajin
    /// basindaki anlamli kisma dayanir.
    /// </summary>
    private const int MaxNormalizedLength = 500;

    /// <summary>
    /// Bir hata mesajini normallestirir ve SHA-256 ozetini kucuk harf onaltilik
    /// dizge olarak dondurur.
    /// </summary>
    /// <param name="message">Ham hata mesaji.</param>
    /// <returns>Kucuk harf onaltilik SHA-256 ozeti.</returns>
    public static string Compute(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var normalized = GuidPattern().Replace(message, "{guid}");
        normalized = NumberPattern().Replace(normalized, "{n}");
        normalized = TimestampPattern().Replace(normalized, "{ts}");
        normalized = DateOnlyPattern().Replace(normalized, "{ts}");

        if (normalized.Length > MaxNormalizedLength)
        {
            normalized = normalized[..MaxNormalizedLength];
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));

        // Convert.ToHexStringLower net9.0'da eklendi; repo net8.0'i da hedefler.
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [GeneratedRegex(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NumberPattern();

    // Sayi degistirmesinden SONRA calisir: "2026-08-06T10:15:30.123Z" onceki
    // adimda "{n}-{n}-{n}T{n}:{n}:{n}.{n}Z" olur, bu desen onu tek {ts}'e coker.
    [GeneratedRegex(
        @"\{n\}-\{n\}-\{n\}T\{n\}:\{n\}:\{n\}(?:\.\{n\})?Z?",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TimestampPattern();

    [GeneratedRegex(@"\{n\}-\{n\}-\{n\}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DateOnlyPattern();
}
