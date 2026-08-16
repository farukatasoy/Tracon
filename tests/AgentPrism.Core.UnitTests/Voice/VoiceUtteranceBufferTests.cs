using System.Buffers.Binary;

namespace AgentPrism.Core.UnitTests.Voice;

/// <summary>
/// Validates the utterance buffer and WAV wrapping.
/// </summary>
public sealed class VoiceUtteranceBufferTests
{
    [Fact]
    public void Raw_PCM_is_wrapped_into_a_valid_WAV_file()
    {
        // 🚨 Raw PCM has no header and is NOT a file by itself: the decoding
        // endpoint accepts it as a file inside a multipart request and
        // identifies its type from the header.
        var buffer = Create(VoiceAudioFormats.Pcm16, maxBytes: 1_000_000);

        buffer.Append(new byte[3200]);

        var utterance = buffer.Take().ShouldNotBeNull();

        utterance.MediaType.ShouldBe("audio/wav");
        utterance.Data.Length.ShouldBe(3200 + 44);

        var header = utterance.Data.AsSpan();

        header[..4].SequenceEqual("RIFF"u8).ShouldBeTrue();
        header.Slice(8, 4).SequenceEqual("WAVE"u8).ShouldBeTrue();
        header.Slice(36, 4).SequenceEqual("data"u8).ShouldBeTrue();

        BinaryPrimitives.ReadUInt32LittleEndian(header[24..]).ShouldBe(16_000u);
        BinaryPrimitives.ReadInt16LittleEndian(header[22..]).ShouldBe((short)1);
        BinaryPrimitives.ReadInt16LittleEndian(header[34..]).ShouldBe((short)16);
        BinaryPrimitives.ReadUInt32LittleEndian(header[40..]).ShouldBe(3200u);
    }

    [Fact]
    public void Raw_PCM_duration_is_computed_from_the_byte_count()
    {
        // 16 kHz, mono, 16-bit -> 32,000 bytes per second.
        var buffer = Create(VoiceAudioFormats.Pcm16, maxBytes: 1_000_000);

        buffer.Append(new byte[32_000]);

        buffer.Take().ShouldNotBeNull().Duration.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Compressed_container_duration_is_unknown()
    {
        // Duration can only be determined by decoding; rather than guessing,
        // the value reported by the provider is used (K-032).
        var buffer = Create(VoiceAudioFormats.WebmOpus, maxBytes: 1_000_000);

        buffer.Append(new byte[1024]);

        var utterance = buffer.Take().ShouldNotBeNull();

        utterance.Duration.ShouldBeNull();
        utterance.MediaType.ShouldBe("audio/webm");
    }

    [Fact]
    public void Buffer_fills_once_the_byte_limit_is_exceeded_and_no_new_chunk_is_accepted()
    {
        var buffer = Create(VoiceAudioFormats.WebmOpus, maxBytes: 2048);

        buffer.Append(new byte[4096]).ShouldBeTrue();
        buffer.Length.ShouldBe(2048);
        buffer.IsFull.ShouldBeTrue();

        buffer.Append(new byte[10]).ShouldBeFalse();
        buffer.Length.ShouldBe(2048);
    }

    [Fact]
    public void PCM_duration_limit_can_fill_before_the_byte_limit()
    {
        // Safety net: if the client's VAD never triggers, the chunk closes
        // on its own at the duration limit.
        var buffer = new VoiceUtteranceBuffer(
            VoiceAudioFormats.Pcm16,
            sampleRate: 16_000,
            maxBytes: 10_000_000,
            maxDuration: TimeSpan.FromSeconds(1));

        buffer.Append(new byte[16_000]);
        buffer.IsFull.ShouldBeFalse();

        buffer.Append(new byte[16_000]);
        buffer.IsFull.ShouldBeTrue();
    }

    [Fact]
    public void Empty_buffer_produces_no_chunk()
    {
        var buffer = Create(VoiceAudioFormats.WebmOpus, maxBytes: 4096);

        buffer.HasAudio.ShouldBeFalse();
        buffer.Take().ShouldBeNull();
    }

    [Fact]
    public void Take_empties_the_buffer()
    {
        var buffer = Create(VoiceAudioFormats.WebmOpus, maxBytes: 4096);

        buffer.Append(new byte[100]);
        buffer.Take().ShouldNotBeNull();

        buffer.Length.ShouldBe(0);
        buffer.Take().ShouldBeNull();
    }

    [Fact]
    public void Clear_discards_the_accumulated_audio()
    {
        var buffer = Create(VoiceAudioFormats.WebmOpus, maxBytes: 4096);

        buffer.Append(new byte[100]);
        buffer.Clear();

        buffer.HasAudio.ShouldBeFalse();
    }

    private static VoiceUtteranceBuffer Create(string format, int maxBytes)
        => new(format, sampleRate: 16_000, maxBytes: maxBytes, maxDuration: TimeSpan.FromSeconds(60));
}
