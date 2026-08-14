using System.Globalization;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Bir ek adayini boyut ve tur beyaz listesine karsi dogrular.</summary>
/// <remarks>
/// <para>
/// Istemcinin bildirdigi <c>Content-Type</c> kanit sayilmaz. Tur, iceriğin ilk
/// baytlarindaki imzadan (sihirli bayt) cikarilir; bildirilen deger yalnizca
/// dosya adi uzantisi gibi bilgilendirme amaclidir ve yok sayilir.
/// </para>
/// <para>
/// Yurutulebilir icerik turleri (ornegin <c>application/x-msdownload</c>)
/// beyaz listede BILEREK yoktur ve hicbir sihirli bayt kurali onlari uretmez;
/// dolayisiyla boyle bir dosya her zaman reddedilir.
/// </para>
/// </remarks>
public sealed class AttachmentTypeGuard
{
    private readonly AgentPrismAttachmentOptions _options;

    /// <summary>Yapilandirmadan yeni bir denetleyici olusturur.</summary>
    /// <param name="options">AgentPrism ayarlari.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> <see langword="null"/> ise.</exception>
    public AttachmentTypeGuard(IOptions<AgentPrismOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value.Attachments;
    }

    /// <summary>Yapilandirmadaki ek boyutu sinirini dondurur.</summary>
    public long MaxBytes => _options.MaxBytes;

    /// <summary>
    /// Icerigi dogrular: bos degil, boyut sinirinin altinda ve sihirli bayti
    /// beyaz listedeki bir turle esleseiyor mu.
    /// </summary>
    /// <param name="data">Denetlenecek ham icerik.</param>
    /// <returns>Gecerliyse dogrulanmis tur, degilse gerekce.</returns>
    public AttachmentValidationResult Validate(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return AttachmentValidationResult.Invalid("The attachment cannot be empty.");
        }

        if (data.Length > _options.MaxBytes)
        {
            return AttachmentValidationResult.Invalid(
                $"Attachment size exceeds the {_options.MaxBytes.ToString("N0", CultureInfo.InvariantCulture)} byte limit.");
        }

        if (SniffMediaType(data) is not { } sniffed)
        {
            return AttachmentValidationResult.Invalid(
                "File type not recognized. Supported types: " +
                string.Join(", ", _options.AllowedMediaTypes.OrderBy(static value => value, StringComparer.Ordinal)) + ".");
        }

        return IsAllowed(sniffed)
            ? AttachmentValidationResult.Valid(sniffed)
            : AttachmentValidationResult.Invalid($"'{sniffed}' is not an allowed type.");
    }

    private bool IsAllowed(string mediaType)
    {
        foreach (var allowed in _options.AllowedMediaTypes)
        {
            if (string.Equals(allowed, mediaType, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (allowed.EndsWith("/*", StringComparison.Ordinal) &&
                mediaType.StartsWith(allowed[..^1], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Ilk baytlardan bilinen bir dosya imzasini cikarir.</summary>
    /// <remarks>
    /// Yeni bir ikili tur eklemek icin buraya bir imza eklemek yeterlidir; beyaz
    /// listeye eklenmeyen bir tur yine de reddedilir.
    /// </remarks>
    private static string? SniffMediaType(ReadOnlySpan<byte> data)
    {
        if (StartsWith(data, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (StartsWith(data, [0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (StartsWith(data, "GIF87a"u8) || StartsWith(data, "GIF89a"u8))
        {
            return "image/gif";
        }

        if (data.Length >= 12 &&
            StartsWith(data, "RIFF"u8) &&
            data.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        if (StartsWith(data, "%PDF-"u8))
        {
            return "application/pdf";
        }

        if (data.Length >= 12 && StartsWith(data, "RIFF"u8) && data.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            return "audio/wav";
        }

        if (StartsWith(data, "OggS"u8))
        {
            return "audio/ogg";
        }

        if (StartsWith(data, "ID3"u8) || IsMpegFrameSync(data))
        {
            return "audio/mpeg";
        }

        // text/plain icin guvenilir bir sihirli bayt yoktur: gecerli UTF-8 olan ve
        // ilk 1 KB'inda kontrol/NUL baytı tasimayan icerik metin sayilir.
        return LooksLikePlainText(data) ? "text/plain" : null;
    }

    /// <summary>Bir MPEG ses cercevesi basligini tanir (ID3 etiketi olmayan MP3).</summary>
    /// <remarks>
    /// <para>
    /// Cerceve senkronu <strong>11 bit 1</strong>'dir: ilk bayt <c>0xFF</c>, ikinci
    /// baytin ust uc biti <c>111</c>. Ikinci bayttaki kalan bitler surum ve katman
    /// alanlaridir ve cok sayida gecerli deger uretir — <c>0xFB</c>, <c>0xF3</c>,
    /// <c>0xF2</c>, <c>0xFA</c>, <c>0xE3</c> gibi. Bu degerleri tek tek listelemek
    /// gercek cikti bicimine gore SESSIZ bir ret uretir; kural bit maskesiyle
    /// yazilir.
    /// </para>
    /// <para>
    /// Yanlis eslesmeyi onlemek icin surum ve katman alanlari da denetlenir:
    /// ikisinin de <c>reserved</c> degeri (sirasiyla <c>01</c> ve <c>00</c>) gecerli
    /// bir cerceve degildir. Bu denetim olmadan <c>FF E0</c> ile baslayan her ikili
    /// icerik ses sayilirdi.
    /// </para>
    /// <para>
    /// Gerekce: <c>docs/28-SES-TOOLLARI.md</c>, bolum 28.0/G3.
    /// </para>
    /// </remarks>
    private static bool IsMpegFrameSync(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2 || data[0] != 0xFF || (data[1] & 0xE0) != 0xE0)
        {
            return false;
        }

        var version = (data[1] >> 3) & 0x03;   // 01 = ayrilmis
        var layer = (data[1] >> 1) & 0x03;     // 00 = ayrilmis

        return version != 0x01 && layer != 0x00;
    }

    private static bool LooksLikePlainText(ReadOnlySpan<byte> data)
    {
        var sample = data.Length > 1024 ? data[..1024] : data;

        // Yatay sekme, satir sonu ve satirbasi disindaki her kontrol baytı
        // ikili icerige isaret eder.
        foreach (var b in sample)
        {
            if (b < 0x20 && b is not (0x09 or 0x0A or 0x0D))
            {
                return false;
            }
        }

        try
        {
            _ = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(sample);
            return true;
        }
        catch (System.Text.DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
        => data.Length >= signature.Length && data[..signature.Length].SequenceEqual(signature);
}

/// <summary>Ek dogrulamasinin sonucu.</summary>
public readonly struct AttachmentValidationResult
{
    private AttachmentValidationResult(bool isValid, string? mediaType, string? error)
    {
        IsValid = isValid;
        MediaType = mediaType;
        Error = error;
    }

    /// <summary>Icerik gecerli mi.</summary>
    public bool IsValid { get; }

    /// <summary>Gecerliyse sihirli bayttan cikarilan MIME turu.</summary>
    public string? MediaType { get; }

    /// <summary>Gecersizse kullaniciya gosterilecek gerekce.</summary>
    public string? Error { get; }

    /// <summary>Basarili bir dogrulama sonucu olusturur.</summary>
    public static AttachmentValidationResult Valid(string mediaType) => new(true, mediaType, null);

    /// <summary>Basarisiz bir dogrulama sonucu olusturur.</summary>
    public static AttachmentValidationResult Invalid(string error) => new(false, null, error);
}
