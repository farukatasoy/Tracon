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

        var presented = authorizationHeader.AsSpan(BearerPrefix.Length).Trim();

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
