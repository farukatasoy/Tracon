using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Attachments;

/// <summary>
/// <see cref="AttachmentTypeGuard"/>'in sihirli bayt denetimi, boyut siniri ve
/// beyaz liste davranisi.
/// </summary>
public sealed class AttachmentTypeGuardTests
{
    [Theory]
    [MemberData(nameof(KnownSignatures))]
    public void Bilinen_imzalar_dogru_turle_eslesir(byte[] data, string expectedMediaType)
    {
        var guard = CreateGuard();

        var result = guard.Validate(data);

        result.IsValid.ShouldBeTrue();
        result.MediaType.ShouldBe(expectedMediaType);
    }

    [Fact]
    public void Bos_icerik_reddedilir()
    {
        var guard = CreateGuard();

        guard.Validate([]).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Boyut_sinirini_asan_icerik_reddedilir()
    {
        var guard = CreateGuard(options => options.MaxBytes = 10);

        var result = guard.Validate(Png(20));

        result.IsValid.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
    }

    [Fact]
    public void Bilinmeyen_imza_reddedilir()
    {
        var guard = CreateGuard();

        // "MZ" Windows yurutulebilir baslangicidir; hicbir beyaz liste kuralinda yok.
        var result = guard.Validate([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Istemcinin_bildirdigi_tur_yok_sayilir_sihirli_bayt_esas_alinir()
    {
        // Istemci "image/png" iddia etse bile icerik gercekte JPEG imzasi
        // tasiyorsa dogru tur (JPEG) donmelidir; Content-Type kanit sayilmaz.
        var guard = CreateGuard();

        var result = guard.Validate(Jpeg());

        result.MediaType.ShouldBe("image/jpeg");
    }

    [Fact]
    public void Beyaz_listeden_cikarilan_tur_taninsa_bile_reddedilir()
    {
        var guard = CreateGuard(options =>
        {
            options.AllowedMediaTypes.Clear();
            options.AllowedMediaTypes.Add("image/png");
        });

        var result = guard.Validate(Gif());

        result.IsValid.ShouldBeFalse();
        (result.Error ?? string.Empty).ShouldContain("image/gif");
    }

    [Theory]
    [InlineData("audio/wav")]
    [InlineData("audio/ogg")]
    [InlineData("audio/mpeg")]
    public void Audio_joker_kurali_alt_turleri_kapsar(string mediaType)
    {
        var guard = CreateGuard();

        var data = mediaType switch
        {
            "audio/wav" => Wav(),
            "audio/ogg" => Ogg(),
            _ => Mp3(),
        };

        guard.Validate(data).IsValid.ShouldBeTrue();
    }

    public static TheoryData<byte[], string> KnownSignatures() => new()
    {
        { Png(), "image/png" },
        { Jpeg(), "image/jpeg" },
        { Gif(), "image/gif" },
        { Webp(), "image/webp" },
        { Pdf(), "application/pdf" },
        { PlainText(), "text/plain" },
        { Wav(), "audio/wav" },
        { Ogg(), "audio/ogg" },
        { Mp3(), "audio/mpeg" },
    };

    private static AttachmentTypeGuard CreateGuard(Action<AgentPrismAttachmentOptions>? configure = null)
    {
        var options = new AgentPrismOptions();
        configure?.Invoke(options.Attachments);
        return new AttachmentTypeGuard(Options.Create(options));
    }

    private static byte[] Png(int paddingBytes = 4)
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        return [.. signature, .. new byte[paddingBytes]];
    }

    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    private static byte[] Gif() => "GIF89a"u8.ToArray().Concat(new byte[4]).ToArray();

    private static byte[] Webp()
        => "RIFF"u8.ToArray().Concat(new byte[4]).Concat("WEBP"u8.ToArray()).Concat(new byte[4]).ToArray();

    private static byte[] Pdf() => "%PDF-1.7\n%merhaba\n"u8.ToArray();

    private static byte[] PlainText() => "Merhaba dunya, bu bir metin dosyasidir.\n"u8.ToArray();

    private static byte[] Wav()
        => "RIFF"u8.ToArray().Concat(new byte[4]).Concat("WAVE"u8.ToArray()).Concat(new byte[4]).ToArray();

    private static byte[] Ogg() => "OggS"u8.ToArray().Concat(new byte[4]).ToArray();

    private static byte[] Mp3() => "ID3"u8.ToArray().Concat(new byte[4]).ToArray();
}
