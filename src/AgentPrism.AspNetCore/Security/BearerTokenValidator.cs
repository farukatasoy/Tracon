using System.Security.Cryptography;
using System.Text;

namespace AgentPrism;

/// <summary>
/// <c>Authorization: Bearer</c> basligini beklenen token ile sabit zamanda
/// karsilastirir.
/// </summary>
/// <remarks>
/// <para>
/// Karsilastirma once her iki degerin SHA-256 ozetini alir, sonra ozetleri
/// <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>
/// ile esler. Ham baytlari dogrudan karsilastirmak <strong>uzunlugu sizdirirdi</strong>:
/// farkli uzunluktaki girdilerde <c>FixedTimeEquals</c> hemen doner ve saldirgan
/// token uzunlugunu olcebilir. Ozet her zaman 32 bayttir, dolayisiyla sure girdiden
/// bagimsizdir.
/// </para>
/// </remarks>
internal static class BearerTokenValidator
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>Basligin beklenen token'i tasidigini dogrular.</summary>
    /// <param name="authorizationHeader">Ham <c>Authorization</c> basligi.</param>
    /// <param name="expectedToken">Beklenen token.</param>
    /// <returns>Baslik gecerliyse <see langword="true"/>.</returns>
    public static bool IsValid(string? authorizationHeader, string expectedToken)
    {
        if (string.IsNullOrEmpty(authorizationHeader) ||
            !authorizationHeader.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return IsValidToken(authorizationHeader.AsSpan(BearerPrefix.Length).Trim(), expectedToken);
    }

    /// <summary>Ham bir token degerini beklenen token ile karsilastirir.</summary>
    /// <param name="presented">Sunulan token.</param>
    /// <param name="expectedToken">Beklenen token.</param>
    /// <returns>Degerler esitse <see langword="true"/>.</returns>
    /// <remarks>
    /// 🚨 WebSocket el sikismasi icindir: tarayici bir <c>&lt;script&gt;</c> veya
    /// soket yukseltmesine <c>Authorization</c> basligi <strong>ekleyemez</strong>,
    /// bu yuzden token <c>Sec-WebSocket-Protocol</c> alt protokolunde tasinir ve
    /// buraya cıplak gelir. Karsilastirma yine sabit zamanlidir.
    /// </remarks>
    public static bool IsValidToken(ReadOnlySpan<char> presented, string expectedToken)
    {
        if (presented.IsEmpty)
        {
            return false;
        }

        Span<byte> presentedHash = stackalloc byte[SHA256.HashSizeInBytes];
        Span<byte> expectedHash = stackalloc byte[SHA256.HashSizeInBytes];

        HashUtf8(presented, presentedHash);
        HashUtf8(expectedToken.AsSpan(), expectedHash);

        return CryptographicOperations.FixedTimeEquals(presentedHash, expectedHash);
    }

    /// <summary>Bir <c>Authorization</c> basligindan ham token degerini cikarir.</summary>
    /// <param name="authorizationHeader">Ham baslik.</param>
    /// <returns>Token; baslik <c>Bearer</c> semasi tasimiyorsa veya bossa <see langword="null"/>.</returns>
    /// <remarks>
    /// Degerin gecerli oldugunu DOGRULAMAZ — yalnizca ayiklar. Cagiran, statik
    /// token karsilastirmasindan sonra API anahtari yolunu denemek icin bunu
    /// kullanir (Faz 53).
    /// </remarks>
    public static string? TryExtractToken(string? authorizationHeader)
    {
        if (string.IsNullOrEmpty(authorizationHeader) ||
            !authorizationHeader.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var candidate = authorizationHeader[BearerPrefix.Length..].Trim();

        return candidate.Length == 0 ? null : candidate;
    }

    private static void HashUtf8(ReadOnlySpan<char> value, Span<byte> destination)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        var buffer = byteCount <= 256 ? stackalloc byte[256] : new byte[byteCount];
        var written = Encoding.UTF8.GetBytes(value, buffer);

        SHA256.HashData(buffer[..written], destination);

        // Token baytlarini yigin uzerinde birakma.
        CryptographicOperations.ZeroMemory(buffer[..written]);
    }
}
