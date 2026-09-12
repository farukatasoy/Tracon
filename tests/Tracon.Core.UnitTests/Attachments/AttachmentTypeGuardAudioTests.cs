using Microsoft.Extensions.Options;
using Shouldly;

namespace Tracon.Core.UnitTests.Attachments;

/// <summary>
/// Verifies that audio types are recognized from their magic bytes.
/// </summary>
/// <remarks>
/// Phase 28 finding G3: the list used to recognize only <c>ID3</c>, <c>FF FB</c>,
/// and <c>FF F3</c>. The MPEG frame sync is an 11-bit MASK and produces many
/// valid second-byte values; a fixed list produced a SILENT rejection on real
/// provider output.
/// </remarks>
public sealed class AttachmentTypeGuardAudioTests
{
    private static readonly AttachmentTypeGuard Guard =
        new(Options.Create(new TraconOptions()));

    [Theory]
    // Tagged MP3.
    [InlineData(new byte[] { 0x49, 0x44, 0x33, 0x04, 0x00 }, "ID3 tag")]
    // Frame sync — MPEG1 Layer III.
    [InlineData(new byte[] { 0xFF, 0xFB, 0x90, 0x00 }, "MPEG1 Layer III")]
    // Frame sync — MPEG2 Layer III.
    [InlineData(new byte[] { 0xFF, 0xF3, 0x48, 0xC4 }, "MPEG2 Layer III")]
    // 🚨 Valid frames NOT covered by the old list.
    [InlineData(new byte[] { 0xFF, 0xF2, 0x40, 0x00 }, "MPEG2 Layer II")]
    [InlineData(new byte[] { 0xFF, 0xFA, 0x90, 0x00 }, "MPEG1 Layer III (unprotected)")]
    [InlineData(new byte[] { 0xFF, 0xE3, 0x18, 0x00 }, "MPEG2.5 Layer III")]
    public void Valid_MPEG_headers_are_recognized_as_audio_mpeg(byte[] data, string description)
    {
        var result = Guard.Validate(data);

        result.IsValid.ShouldBeTrue($"{description} was not recognized.");
        string.Equals(result.MediaType, "audio/mpeg", StringComparison.Ordinal)
            .ShouldBeTrue($"{description} was mapped to the wrong type: {result.MediaType}");
    }

    [Theory]
    // Sync is present but the VERSION field is reserved (01) — not a valid frame.
    [InlineData(new byte[] { 0xFF, 0xEB, 0x90, 0x00 })]
    // Sync is present but the LAYER field is reserved (00) — not a valid frame.
    [InlineData(new byte[] { 0xFF, 0xF9, 0x90, 0x00 })]
    public void Header_with_a_reserved_field_is_NOT_counted_as_audio(byte[] data)
    {
        // Loosening the mask would turn any binary content starting with 0xFF into audio.
        var result = Guard.Validate(data);

        (result.MediaType is null || !string.Equals(result.MediaType, "audio/mpeg", StringComparison.Ordinal))
            .ShouldBeTrue($"an invalid frame was counted as audio: {result.MediaType}");
    }

    [Fact]
    public void Raw_PCM_content_is_REJECTED()
    {
        // Voice providers' `pcm_*` output has no header. It also cannot be stored;
        // VoiceOptionsValidator therefore rejects this format up front.
        var pcm = new byte[64];
        Random.Shared.NextBytes(pcm);
        pcm[0] = 0x01;

        var result = Guard.Validate(pcm);

        (result.MediaType is null || !result.MediaType.StartsWith("audio/", StringComparison.Ordinal))
            .ShouldBeTrue($"headerless content was counted as audio: {result.MediaType}");
    }

    [Fact]
    public void WAV_and_OGG_are_still_recognized()
    {
        Guard.Validate("RIFF    WAVEfmt "u8.ToArray()).MediaType.ShouldBe("audio/wav");
        Guard.Validate("OggS   "u8.ToArray()).MediaType.ShouldBe("audio/ogg");
    }
}
