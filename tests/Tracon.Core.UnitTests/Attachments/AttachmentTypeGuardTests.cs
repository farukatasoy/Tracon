using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Attachments;

/// <summary>
/// <see cref="AttachmentTypeGuard"/>'s magic-byte check, size limit, and
/// allow-list behavior.
/// </summary>
public sealed class AttachmentTypeGuardTests
{
    [Theory]
    [MemberData(nameof(KnownSignatures))]
    public void Known_signatures_match_the_correct_type(byte[] data, string expectedMediaType)
    {
        var guard = CreateGuard();

        var result = guard.Validate(data);

        result.IsValid.ShouldBeTrue();
        result.MediaType.ShouldBe(expectedMediaType);
    }

    [Fact]
    public void Empty_content_is_rejected()
    {
        var guard = CreateGuard();

        guard.Validate([]).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Content_over_the_size_limit_is_rejected()
    {
        var guard = CreateGuard(options => options.MaxBytes = 10);

        var result = guard.Validate(Png(20));

        result.IsValid.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
    }

    [Fact]
    public void Unknown_signature_is_rejected()
    {
        var guard = CreateGuard();

        // "MZ" is the Windows executable header; it is not in any allow-list rule.
        var result = guard.Validate([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Client_reported_type_is_ignored_the_magic_bytes_are_authoritative()
    {
        // Even if the client claims "image/png", if the content actually carries
        // a JPEG signature, the correct type (JPEG) must be returned; Content-Type
        // is not treated as evidence.
        var guard = CreateGuard();

        var result = guard.Validate(Jpeg());

        result.MediaType.ShouldBe("image/jpeg");
    }

    [Fact]
    public void A_type_removed_from_the_allow_list_is_rejected_even_when_recognized()
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
    public void Audio_wildcard_rule_covers_subtypes(string mediaType)
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

    private static AttachmentTypeGuard CreateGuard(Action<TraconAttachmentOptions>? configure = null)
    {
        var options = new TraconOptions();
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

    private static byte[] Pdf() => "%PDF-1.7\n%hello\n"u8.ToArray();

    private static byte[] PlainText() => "Hello world, this is a text file.\n"u8.ToArray();

    private static byte[] Wav()
        => "RIFF"u8.ToArray().Concat(new byte[4]).Concat("WAVE"u8.ToArray()).Concat(new byte[4]).ToArray();

    private static byte[] Ogg() => "OggS"u8.ToArray().Concat(new byte[4]).ToArray();

    private static byte[] Mp3() => "ID3"u8.ToArray().Concat(new byte[4]).ToArray();
}
