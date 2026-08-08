using System.Security.Cryptography;
using System.Text;

namespace AgentPrism;

/// <summary>Ham API anahtari degeri ve ozetini ureten yardimci.</summary>
/// <remarks>
/// <para>
/// Bicim: <c>ap_{kiraci-onek}_{32-bayt-base64url}</c>
/// (docs/53-KIRACI-API-ANAHTARLARI.md, bolum 53.2). Onek yalnizca
/// OKUNABILIRLIK icindir; kimlik dogrulamada kullanilmaz — arama her zaman
/// <see cref="ComputeHash"/>'in urettigi ozet uzerinden yapilir.
/// </para>
/// <para>
/// 🚨 Hash algoritmasi SHA-256'dir, Argon2 DEGIL (Acik Soru 2). Anahtar
/// 32 baytlik RASTGELE bir degerdir, kullanici parolasi degildir; sozluk
/// saldirisi soz konusu degildir ve yavas bir hash yalnizca her istege
/// gecikme ekler.
/// </para>
/// </remarks>
public static class ApiKeyGenerator
{
    private const string KeyPrefixMarker = "ap_";
    private const int SecretByteLength = 32;
    private const int DisplayPrefixLength = 12;
    private const int MaxTenantSegmentLength = 12;

    /// <summary>Yeni bir ham anahtar uretir.</summary>
    /// <param name="tenantId">Anahtarin baglanacagi kiraci (yalnizca onek icin okunur).</param>
    /// <returns>Ham deger, ozeti ve goruntuleme oneki.</returns>
    /// <exception cref="ArgumentException"><paramref name="tenantId"/> bos ise.</exception>
    public static GeneratedApiKey Generate(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        Span<byte> secretBytes = stackalloc byte[SecretByteLength];
        RandomNumberGenerator.Fill(secretBytes);

        var plaintext = $"{KeyPrefixMarker}{SanitizeTenantSegment(tenantId)}_{ToBase64Url(secretBytes)}";

        return new GeneratedApiKey
        {
            PlaintextKey = plaintext,
            KeyHash = ComputeHash(plaintext),
            KeyPrefix = plaintext[..Math.Min(DisplayPrefixLength, plaintext.Length)],
        };
    }

    /// <summary>Bir ham anahtar degerinin SHA-256 ozetini hesaplar.</summary>
    /// <param name="plaintextKey">Ham deger.</param>
    /// <returns>32 baytlik ozet.</returns>
    /// <exception cref="ArgumentException"><paramref name="plaintextKey"/> bos ise.</exception>
    public static byte[] ComputeHash(string plaintextKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintextKey);

        return SHA256.HashData(Encoding.UTF8.GetBytes(plaintextKey));
    }

    private static string SanitizeTenantSegment(string tenantId)
    {
        Span<char> buffer = stackalloc char[Math.Min(tenantId.Length, MaxTenantSegmentLength)];
        var written = 0;

        foreach (var ch in tenantId)
        {
            if (written == buffer.Length)
            {
                break;
            }

            if (char.IsAsciiLetterOrDigit(ch))
            {
                buffer[written++] = char.ToLowerInvariant(ch);
            }
        }

        return written == 0 ? "default" : new string(buffer[..written]);
    }

    private static string ToBase64Url(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Uretilen ham API anahtari degeri ve turevleri.</summary>
public sealed record GeneratedApiKey
{
    /// <summary>Ham deger. Yalnizca olusturma aninda gorunur.</summary>
    public required string PlaintextKey { get; init; }

    /// <summary>Ham degerin SHA-256 ozeti. Depoya yazilan tek sekildir.</summary>
    public required byte[] KeyHash { get; init; }

    /// <summary>Ham degerin ilk karakterleri; listede ayirt etmek icindir.</summary>
    public required string KeyPrefix { get; init; }
}
